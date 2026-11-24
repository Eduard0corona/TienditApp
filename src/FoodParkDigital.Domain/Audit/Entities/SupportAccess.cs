using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Audit.Entities;

/// <summary>
/// The platform admin's access to act on one business (Fase 32G, ADR 0099): opened with a reason and for a time, it is what
/// lets the <c>X-Tenant-Id</c> header act on that business. It belongs to the business it opens (its owner sees it in its
/// activity), ends at its time or when the admin closes it, and is never reopened: another access is another row.
/// </summary>
public class SupportAccess : BaseEntity, IAggregateRoot, ITenantScoped
{
    public const int MinReasonLength = 10;
    public const int MaxReasonLength = 500;
    public const int MinMinutes = 15;
    public const int MaxMinutes = 240;

    public const string ReasonMessage = "Escribe por qué entras al negocio (de 10 a 500 caracteres).";
    public const string MinutesMessage = "El acceso dura de 15 minutos a 4 horas.";

    private SupportAccess() { }

    public Guid TenantId { get; private set; }

    /// <summary>The platform admin who opened it: only that account acts with it.</summary>
    public Guid AdminUserId { get; private set; }

    /// <summary>Why the admin enters, as they wrote it: the owner reads it in the activity of the business.</summary>
    public string Reason { get; private set; } = null!;

    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When the admin closed it before its time; null while open or once it expired on its own.</summary>
    public DateTimeOffset? EndedAt { get; private set; }

    public static SupportAccess Start(Guid tenantId, Guid adminUserId, string? reason, int minutes, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || adminUserId == Guid.Empty)
            throw new DomainException("El acceso de soporte necesita un negocio y una cuenta.");
        var cleaned = reason?.Trim();
        if (cleaned is null || cleaned.Length < MinReasonLength || cleaned.Length > MaxReasonLength)
            throw new DomainException(ReasonMessage);
        if (minutes < MinMinutes || minutes > MaxMinutes)
            throw new DomainException(MinutesMessage);

        return new SupportAccess
        {
            TenantId = tenantId,
            AdminUserId = adminUserId,
            Reason = cleaned,
            StartedAt = now,
            ExpiresAt = now.AddMinutes(minutes)
        };
    }

    /// <summary>Lets its admin act on the business now: not closed, and before its time.</summary>
    public bool IsOpenAt(DateTimeOffset now) => EndedAt is null && now < ExpiresAt;

    /// <summary>The admin closes it before its time. Closing one already closed or expired changes nothing.</summary>
    public bool End(DateTimeOffset now)
    {
        if (!IsOpenAt(now))
            return false;
        EndedAt = now;
        return true;
    }
}
