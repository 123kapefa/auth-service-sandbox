namespace SharedKernel;

public static class GeneralErrors
{
    public static Error ValueIsInvalid(string? fieldName = null) =>
        fieldName is null
            ? Error.Validation("value.is.invalid", "Некорректное значение")
            : Error.Validation("value.is.invalid", $"Некорректное значение поля «{fieldName}»", fieldName);

    public static Error ValueIsRequired(string? fieldName = null) =>
        fieldName is null
            ? Error.Validation("value.is.required", "Обязательное поле не заполнено")
            : Error.Validation("value.is.required", $"Поле «{fieldName}» обязательно для заполнения", fieldName);

    public static Error LengthIsInvalid(string? fieldName = null, int? min = null, int? max = null)
    {
        string label = fieldName is null ? "Поле" : $"Поле «{fieldName}»";
        string message = (min, max) switch
        {
            ({ } from, { } to) => $"{label}: допустимая длина от {from} до {to} символов",
            ({ } from, null) => $"{label}: минимальная длина {from} символов",
            (null, { } to) => $"{label}: максимальная длина {to} символов",
            _ => $"{label}: недопустимая длина",
        };

        return Error.Validation("length.is.invalid", message, fieldName);
    }

    public static Error OutOfRange(string? fieldName = null, object? min = null, object? max = null)
    {
        string label = fieldName ?? "Значение";
        string message = (min, max) switch
        {
            ({ } from, { } to) => $"{label}: от {from} до {to}",
            ({ } from, null) => $"{label}: минимум {from}",
            (null, { } to) => $"{label}: максимум {to}",
            _ => $"{label} вне диапазона",
        };

        return Error.Validation("value.out.of.range", message, fieldName);
    }

    public static Error InvalidFormat(string? fieldName = null) =>
        Error.Validation("invalid.format", $"{fieldName ?? "Значение"}: неверный формат", fieldName);

    public static Error InvalidOperation(string? message = null) =>
        Error.Validation("invalid.operation", message ?? "Недопустимая операция");

    public static Error NotFound(string? entityName = null) =>
        Error.NotFound("record.not.found", $"Не удалось найти: {entityName ?? "запись"}");

    public static Error NotFoundBy(string fieldName, string value, string? entityName = null) =>
        Error.NotFound("record.not.found", $"Не удалось найти {entityName ?? "запись"} по {fieldName}: «{value}»");

    public static Error AlreadyExists(string? entityName = null, string? value = null)
    {
        string entity = entityName ?? "Запись";
        return string.IsNullOrEmpty(value)
            ? Error.Conflict("record.already.exists", $"{entity} уже существует")
            : Error.Conflict("record.already.exists", $"{entity} «{value}» уже существует");
    }

    public static Error UniqueConstraintViolation(string? fieldName = null) =>
        Error.Conflict(
            "unique.constraint.violation",
            fieldName is null ? "Значение должно быть уникальным" : $"Значение ({fieldName}) должно быть уникальным",
            fieldName);

    public static Error ForeignKeyViolation(string? fieldName = null) =>
        Error.Conflict("foreign.key.violation", $"{fieldName ?? "Связанная запись"} используется в других данных", fieldName);

    public static Error ConcurrencyConflict() =>
        Error.Conflict("concurrency.conflict", "Данные изменены другим пользователем").AsTransient();

    public static Error DatabaseTimeout() =>
        Error.Failure("database.timeout", "Превышено время ожидания базы данных").AsTransient();

    public static Error DatabaseError() =>
        Error.Failure("database.error", "Ошибка базы данных");

    public static Error OperationCancelled() =>
        Error.Failure("operation.cancelled", "Операция отменена");

    public static Error Failure(string? message = null) =>
        Error.Failure("server.failure", message ?? "Ошибка сервера");

    public static Error Unauthorized() =>
        Error.Authentication("unauthorized", "Требуется авторизация");

    public static Error Forbidden() =>
        Error.Authorization("forbidden", "Доступ запрещён");
}
