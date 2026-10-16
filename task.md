С учётом новых требований (отдельная БД для конфигурации, D3.js, API для списка серверов) план нужно существенно скорректировать. Ключевое изменение — архитектура разделяется на **базу метаданных** (где хранятся строки подключения и история) и **целевые базы PostgreSQL** (откуда собирается статистика).


## 1. Архитектура с отдельной базой данных

### 1.1. Два типа баз данных

| Тип | Назначение | Технология |
|-----|-----------|-----------|
| **Мета-БД** (собственная) | Хранение строк подключения, снимков, истории графиков | PostgreSQL / SQLite + EF Core |
| **Целевые БД** (удалённые) | Источники `pg_stat_statements` | Npgsql (прямые запросы) |

Это разделение принципиально: для мета-БД вы используете EF Core с миграциями, для целевых — сырой Npgsql, так как это системные каталоги.

### 1.2. Схема мета-БД

```
MonitoredDatabase
├── Id (PK, string)          — "prod-main"
├── DisplayName              — "Production"
├── Host, Port, DatabaseName
├── Username, PasswordEncrypted
├── Color                    — "#e74c3c" для графиков
├── IsEnabled
└── CreatedAt

Snapshot
├── Id (PK, long)
├── MonitoredDatabaseId (FK)
├── CapturedAt (timestamp)
└── RawData (jsonb)          — сериализованный снимок pg_stat_statements

MetricPoint
├── Id (PK, long)
├── MonitoredDatabaseId (FK)
├── CapturedAt
├── QueryId (long)
├── Calls, TotalExecTime, MeanExecTime, Rows
└── QueryText (text)
```

**Важно:** строки подключения шифруются перед сохранением. Используйте `IDataProtector` из ASP.NET Core Data Protection для шифрования паролей.


## 2. Запросы к мета-БД (EF Core)

### 2.1. Получение списка баз данных для API

```csharp
// API endpoint: GET /api/databases
public async Task<IActionResult> GetDatabases()
{
    var dbs = await _metaContext.MonitoredDatabases
        .Where(d => d.IsEnabled)
        .Select(d => new {
            d.Id,
            d.DisplayName,
            d.Host,
            d.DatabaseName,
            d.Color,
            LastSnapshot = d.Snapshots
                .OrderByDescending(s => s.CapturedAt)
                .Select(s => s.CapturedAt)
                .FirstOrDefault()
        })
        .ToListAsync();
    
    return Ok(dbs);
}
```

Этот endpoint возвращает список серверов, который фронтенд использует для построения селекторов и легенды графиков.

### 2.2. Получение исторических метрик для D3

```csharp
// API endpoint: GET /api/metrics?dbIds=prod-main,stage-01&from=...&to=...
public async Task<IActionResult> GetMetrics(
    [FromQuery] string[] dbIds,
    [FromQuery] DateTime from,
    [FromQuery] DateTime to)
{
    var data = await _metaContext.MetricPoints
        .Where(m => dbIds.Contains(m.MonitoredDatabaseId) 
                 && m.CapturedAt >= from 
                 && m.CapturedAt <= to)
        .GroupBy(m => new { m.MonitoredDatabaseId, m.QueryId })
        .Select(g => new {
            DbId = g.Key.MonitoredDatabaseId,
            QueryId = g.Key.QueryId,
            QueryText = g.First().QueryText,
            Points = g.OrderBy(p => p.CapturedAt)
                      .Select(p => new { 
                          p.CapturedAt, 
                          p.MeanExecTime,
                          p.Calls 
                      })
        })
        .ToListAsync();
    
    return Ok(data);
}
```

Формат ответа — массив серий, каждая из которых содержит точки для одной БД. Это напрямую ложится на модель данных D3 multi-line chart.


## 3. Интеграция D3.js

### 3.1. Структура данных для мультилинейного графика

D3 ожидает данные в формате, где каждая серия — отдельная БД:

```json
{
  "series": [
    {
      "id": "prod-main",
      "label": "Production",
      "color": "#e74c3c",
      "points": [
        { "time": "2026-10-16T10:00:00Z", "value": 45.2 },
        { "time": "2026-10-16T10:01:00Z", "value": 47.8 }
      ]
    },
    {
      "id": "stage-01",
      "label": "Staging",
      "color": "#3498db",
      "points": [
        { "time": "2026-10-16T10:00:00Z", "value": 12.1 },
        { "time": "2026-10-16T10:01:00Z", "value": 11.5 }
      ]
    }
  ]
}
```

Эта структура соответствует примерам D3 для реального времени: контейнер SVG, линейный генератор с масштабами по X (время) и Y (значение), затем привязка данных и обновление путей при поступлении новых точек .

### 3.2. Рендеринг мультилинейного графика

```javascript
// D3 v7 multi-line chart для сравнения БД
function renderComparisonChart(data) {
  const svg = d3.select("#chart");
  const width = 900, height = 400;
  const margin = { top: 20, right: 120, bottom: 40, left: 60 };
  
  // Масштабы
  const xScale = d3.scaleTime()
    .domain(d3.extent(allPoints, d => d.time))
    .range([margin.left, width - margin.right]);
  
  const yScale = d3.scaleLinear()
    .domain([0, d3.max(allPoints, d => d.value) * 1.1])
    .range([height - margin.bottom, margin.top]);
  
  // Генератор линий
  const line = d3.line()
    .x(d => xScale(d.time))
    .y(d => yScale(d.value))
    .curve(d3.curveMonotoneX);
  
  // Для каждой БД — свой path с цветом
  data.series.forEach(series => {
    svg.append("path")
      .datum(series.points)
      .attr("fill", "none")
      .attr("stroke", series.color)
      .attr("stroke-width", 2)
      .attr("d", line);
  });
}
```

### 3.3. Real-time обновление

Если планируется live-обновление графиков, используйте `setInterval` для периодического запроса новых точек и `d3.transition()` для плавного перемещения линий . Для push-модели лучше SignalR: он автоматически управляет переподключением и fallback на SSE/long-polling, что стандартно для браузерных дашбордов .


## 4. API для списка серверов и данных графиков

| Endpoint | Метод | Назначение |
|----------|-------|-----------|
| `/api/databases` | GET | Список активных БД (для селекторов и легенды) |
| `/api/databases/{id}/test` | POST | Проверка соединения |
| `/api/metrics` | GET | Исторические точки для графиков (фильтр по dbIds, времени, queryId) |
| `/api/snapshot/{dbId}` | GET | Текущий снимок pg_stat_statements |
| `/api/compare` | GET | Сравнение метрик двух и более БД |

### 4.1. Пример ответа `/api/databases`

```json
[
  {
    "id": "prod-main",
    "displayName": "Production Main",
    "host": "pg-prod.internal",
    "databaseName": "app",
    "color": "#e74c3c",
    "lastSnapshotAt": "2026-10-16T10:05:00Z",
    "status": "healthy"
  },
  {
    "id": "stage-01",
    "displayName": "Staging",
    "host": "pg-stage.internal",
    "databaseName": "app",
    "color": "#3498db",
    "lastSnapshotAt": "2026-10-16T10:04:30Z",
    "status": "healthy"
  }
]
```


## 5. Обновлённый пошаговый план

### Этап 1: Мета-БД и управление подключениями (2 недели)

1. **Добавить проект `PgStatView.Meta`** с EF Core DbContext.
2. **Настроить PostgreSQL/SQLite** как хранилище мета-данных.
3. **Реализовать шифрование строк подключения** через `IDataProtector`.
4. **CRUD для `MonitoredDatabase`**: создание, редактирование, удаление, soft-delete.
5. **API `/api/databases`** — список для фронтенда.

### Этап 2: Сбор статистики и снимки (2–3 недели)

1. **`IStatsCollector`** — параллельный опрос всех активных БД через Npgsql.
2. **`BackgroundService`** — периодический сбор (интервал из конфига).
3. **Сохранение `Snapshot` и `MetricPoint`** в мета-БД.
4. **Retention policy** — удаление старых точек (например, старше 30 дней).

### Этап 3: Web UI с D3.js (3 недели)

1. **Главная страница** — список БД с последними метриками.
2. **Мультилинейный график** — сравнение БД по `mean_exec_time` / `calls` / `total_exec_time`.
3. **Селектор БД** — чекбоксы для включения/выключения серий на графике.
4. **Tooltip и hover** — показ точных значений при наведении.
5. **Zoom/pan** — если история длинная, добавьте `d3.zoom` или brush для выбора диапазона .

### Этап 4: Таблицы и детализация (2 недели)

1. **Таблица топ-запросов** по каждой БД с сортировкой.
2. **Фильтр по таблице** — поиск `query ILIKE '%table%'`.
3. **Детальная страница queryid** — история метрик конкретного запроса по всем БД.
4. **Сравнение план vs план** — EXPLAIN для двух БД рядом.

### Этап 5: Визуализация планов (2–3 недели)

1. **Интеграция PEV2** — встроенный JS-компонент для JSON плана.
2. **Endpoint `/api/explain`** — выполняет `EXPLAIN (FORMAT JSON)` на выбранной БД.
3. **Сравнение планов** — два PEV2 side-by-side.

### Этап 6: Real-time и полировка (2 недели)

1. **SignalR hub** для push-обновлений графиков (опционально, если нужен live-режим).
2. **Docker + docker-compose** — приложение + мета-БД PostgreSQL.
3. **Health checks** — проверка доступности всех целевых БД.


## 6. Ключевые риски с учётом новых требований

| Риск | Митигация |
|------|-----------|
| **queryid нестабилен между серверами** — для сравнения используйте нормализованный текст запроса, а не queryid  | Сопоставление по `query` после замены констант |
| **Утечка паролей из мета-БД** | Шифрование через Data Protection API, не логировать connection strings |
| **Рост мета-БД при частом сборе** | Retention policy + партиционирование таблицы `MetricPoint` по дате |
| **D3 не справляется с большим объёмом точек** | Агрегация на сервере (5-минутные корзины для длинных периодов) |
| **EXPLAIN ANALYZE на продакшене** | Только `EXPLAIN (FORMAT JSON)` без ANALYZE, либо на репликах |


## 7. Итоговая структура решения

```
PgStatView.sln
├── PgStatView.Web              (MVC, API, SignalR)
│   ├── Controllers/
│   ├── Views/                  (D3.js в wwwroot/js)
│   └── wwwroot/js/charts/      (d3-comparison.js, d3-single.js)
├── PgStatView.Meta             (EF Core: MonitoredDatabase, Snapshot, MetricPoint)
├── PgStatView.Core             (Domain models, интерфейсы)
├── PgStatView.Collector        (Npgsql: сбор с целевых БД)
└── PgStatView.Tests
```

D3.js даёт максимальную гибкость для кастомных сравнений между БД, но требует написания рендеринга вручную. Если интерактивность (zoom, brush, tooltips) критична, закладывайте время на frontend-разработку отдельно от backend.