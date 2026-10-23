using System;
using System.Collections.Generic;

namespace PgStatView.Core.Models;

/// <summary>
/// Целевой PostgreSQL-сервер для мониторинга.
/// </summary>
public class MonitoredDatabase
{
    /// <summary>
    /// Уникальный идентификатор (slug): "prod-main", "stage-01".
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Отображаемое имя для UI.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Имя хоста или IP-адрес.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Порт PostgreSQL.
    /// </summary>
    public int Port { get; set; } = 5432;

    /// <summary>
    /// Имя базы данных.
    /// </summary>
    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>
    /// Имя пользователя для подключения.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Пароль — хранится открыто (по требованию заказчика).
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Цвет для графиков D3 (#e74c3c и т.п.).
    /// </summary>
    public string Color { get; set; } = "#3498db";

    /// <summary>
    /// Флаг активности. Неактивные БД не опрашиваются.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Статус последнего сбора: "ok", "error", "unreachable".
    /// </summary>
    public string? LastCollectStatus { get; set; }

    /// <summary>
    /// Текст ошибки последнего сбора.
    /// </summary>
    public string? LastCollectError { get; set; }

    /// <summary>
    /// Время последнего успешного сбора (UTC).
    /// </summary>
    public DateTime? LastCollectedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Навигационные свойства
    public ICollection<Snapshot> Snapshots { get; set; } = new List<Snapshot>();
    public ICollection<MetricPoint> MetricPoints { get; set; } = new List<MetricPoint>();
    public ICollection<ExplainPlan> ExplainPlans { get; set; } = new List<ExplainPlan>();

    /// <summary>
    /// Формирует Npgsql-строку подключения.
    /// </summary>
    public string GetConnectionString() =>
        $"Host={Host};Port={Port};Database={DatabaseName};Username={Username};Password={Password};";
}
