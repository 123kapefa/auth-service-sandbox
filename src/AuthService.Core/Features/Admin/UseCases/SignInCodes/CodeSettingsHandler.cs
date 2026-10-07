using System.Globalization;
using AuthService.Contracts.Admin;
using AuthService.Core.Abstractions;
using AuthService.Domain.AuditLog;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.SignInCodes;

/// <summary>
/// Коды подтверждения проекта: способы отправки на телефон, страны, дневной бюджет и лимиты защиты от накрутки.
/// Хранятся в Keycloak (realm), меняются через плагин platform-codes; коды отправляются по новым настройкам со
/// следующей отправки. Изменение — в журнал (что поменялось). Расход за сегодня — оттуда же.
/// </summary>
public sealed class CodeSettingsHandler(IProjectCodes codes, IValidator<SetCodeSettingsRequest> validator, AdminAudit audit)
{
    /// <summary>Имя «объекта» в журнале: раздел админки.</summary>
    public const string AUDIT_NAME = "Коды подтверждения";

    public async Task<Result<CodeSettingsResponse, Error>> GetAsync(CancellationToken cancellationToken)
    {
        Result<ProjectCodes, Error> current = await codes.GetAsync(cancellationToken);
        return current.IsSuccess ? ToResponse(current.Value) : current.Error;
    }

    public async Task<Result<CodeSettingsResponse, Error>> SetAsync(SetCodeSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidationResult validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        Result<ProjectCodes, Error> current = await codes.GetAsync(cancellationToken);
        if (current.IsFailure)
        {
            return current.Error;
        }

        // Звонок всегда первым — порядок в настройке не важен, храним одинаково.
        var wanted = new ProjectCodeSettings(
            CodeSettingsRules.PhoneChannels.Where(channel => request.PhoneChannels!.Contains(channel, StringComparer.Ordinal)).ToList(),
            [.. request.Countries!],
            request.DailyBudget!.Value,
            request.TargetPerHour!.Value,
            request.TargetPerDay!.Value,
            request.IpPerHour!.Value,
            request.RangePerHour!.Value,
            request.CaptchaAfter!.Value);
        Dictionary<string, object?> changes = Changes(current.Value.Settings, wanted);
        if (changes.Count == 0)
        {
            return ToResponse(current.Value);
        }

        Result<ProjectCodes, Error> saved = await codes.SetAsync(wanted, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        await audit.RecordAsync(
            AuditActions.PROJECT_CODES_CHANGED,
            AuditActions.PROJECT_OBJECT,
            Guid.Empty,
            AUDIT_NAME,
            changes,
            CancellationToken.None);
        return ToResponse(saved.Value);
    }

    public async Task<Result<CodeUsageResponse, Error>> UsageAsync(CancellationToken cancellationToken)
    {
        Result<ProjectCodeUsage, Error> usage = await codes.GetUsageAsync(cancellationToken);
        if (usage.IsFailure)
        {
            return usage.Error;
        }

        ProjectCodeUsage day = usage.Value;
        return new CodeUsageResponse(
            day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            day.Zone,
            new CodeBudgetUsage(day.Spent, day.Budget, day.AlertPercent, day.Exhausted),
            new CodeChannelUsage(day.Call.Sent, day.Call.Cost),
            new CodeChannelUsage(day.Sms.Sent, day.Sms.Cost),
            new CodeChannelUsage(day.Email.Sent, day.Email.Cost),
            day.Sent,
            day.Verified,
            day.VerifiedPercent);
    }

    // Только изменённые поля, в виде { old, new }, как в остальном журнале.
    private static Dictionary<string, object?> Changes(ProjectCodeSettings old, ProjectCodeSettings wanted)
    {
        var changes = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!old.PhoneChannels.SequenceEqual(wanted.PhoneChannels, StringComparer.Ordinal))
        {
            changes["phoneChannels"] = new { old = old.PhoneChannels, @new = wanted.PhoneChannels };
        }

        if (!old.Countries.SequenceEqual(wanted.Countries, StringComparer.Ordinal))
        {
            changes["countries"] = new { old = old.Countries, @new = wanted.Countries };
        }

        Add(changes, "dailyBudget", old.DailyBudget, wanted.DailyBudget);
        Add(changes, "targetPerHour", old.TargetPerHour, wanted.TargetPerHour);
        Add(changes, "targetPerDay", old.TargetPerDay, wanted.TargetPerDay);
        Add(changes, "ipPerHour", old.IpPerHour, wanted.IpPerHour);
        Add(changes, "rangePerHour", old.RangePerHour, wanted.RangePerHour);
        Add(changes, "captchaAfter", old.CaptchaAfter, wanted.CaptchaAfter);
        return changes;
    }

    private static void Add(Dictionary<string, object?> changes, string name, long old, long wanted)
    {
        if (old != wanted)
        {
            changes[name] = new { old, @new = wanted };
        }
    }

    private static CodeSettingsResponse ToResponse(ProjectCodes codes) =>
        new(
            codes.Settings.PhoneChannels,
            codes.Settings.Countries,
            codes.Settings.DailyBudget,
            codes.Settings.TargetPerHour,
            codes.Settings.TargetPerDay,
            codes.Settings.IpPerHour,
            codes.Settings.RangePerHour,
            codes.Settings.CaptchaAfter,
            codes.Server.RangeDigits,
            codes.Server.SmsCost,
            codes.Server.CallCost,
            codes.Server.BudgetAlertPercent,
            codes.Server.Zone);
}
