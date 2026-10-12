using FoodParkDigital.Application.Reports.ReadModels;
using FluentValidation;

namespace FoodParkDigital.Application.Reports;

/// <summary>A report over local dates of the business (<see cref="DailySalesReadModel.ResolveRange"/> fills what is omitted).</summary>
internal interface IReportDateRange
{
    DateOnly? From { get; }
    DateOnly? To { get; }
}

/// <summary>The rules every report over local dates shares: sales by branch, sales by channel and the orders of a platform.</summary>
internal static class ReportRangeRules
{
    public const string BackwardsMessage = "La fecha inicial debe ser anterior a la final.";
    public static readonly string TooLongMessage = $"El periodo puede abarcar como máximo {DailySalesReadModel.MaxRangeDays} días.";

    public static void ValidateTheRange<T>(this AbstractValidator<T> validator) where T : IReportDateRange
    {
        validator.RuleFor(x => x)
            .Must(x => x.From is null || x.To is null || x.From <= x.To)
            .WithMessage(BackwardsMessage)
            .Must(x => x.From is null || x.To is null || x.To.Value.DayNumber - x.From.Value.DayNumber < DailySalesReadModel.MaxRangeDays)
            .WithMessage(TooLongMessage);
    }
}
