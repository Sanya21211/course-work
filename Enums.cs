namespace ServiceCenter.Web.Domain;

/// <summary>Роль користувача: набір дозволених операцій.</summary>
public enum UserRole
{
    Client = 1,
    Receptionist = 2,
    Technician = 3
}

/// <summary>Стан заявки сервісного центру.</summary>
public enum RequestStatus
{
    New = 1,
    Assigned = 2,
    Diagnosed = 3,
    InProgress = 4,
    Completed = 5,
    Cancelled = 6
}

/// <summary>Стан окремої роботи з переліку робіт.</summary>
public enum WorkItemStatus
{
    Proposed = 1,
    Approved = 2,
    Rejected = 3,
    Done = 4
}

public static class EnumTitles
{
    public static string Title(this RequestStatus s) => s switch
    {
        RequestStatus.New => "Нова",
        RequestStatus.Assigned => "Призначена виконавцю",
        RequestStatus.Diagnosed => "Очікує погодження робіт",
        RequestStatus.InProgress => "Виконується",
        RequestStatus.Completed => "Завершена",
        RequestStatus.Cancelled => "Скасована",
        _ => s.ToString()
    };

    public static string Title(this WorkItemStatus s) => s switch
    {
        WorkItemStatus.Proposed => "Запропонована",
        WorkItemStatus.Approved => "Погоджена",
        WorkItemStatus.Rejected => "Відхилена",
        WorkItemStatus.Done => "Виконана",
        _ => s.ToString()
    };

    public static string Title(this UserRole r) => r switch
    {
        UserRole.Client => "Клієнт",
        UserRole.Receptionist => "Приймальник",
        UserRole.Technician => "Виконавець",
        _ => r.ToString()
    };
}
