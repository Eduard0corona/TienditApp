using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.Events;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.HomeDelivery;

namespace FoodParkDigital.Domain.Appointments.Entities;

public class Appointment : AuditableEntity, ITenantScoped, IAggregateRoot
{
    /// <summary>The longest name of who attends it that an appointment keeps.</summary>
    public const int MaxStaffNameLength = 120;

    private Appointment() { }

    public Appointment(
        Guid tenantId,
        string customerName,
        string customerPhone,
        string serviceName,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        string? staffName = null,
        string? notes = null,
        Guid? serviceProductId = null,
        Guid? staffUserId = null)
    {
        if (string.IsNullOrWhiteSpace(customerName))
            throw new DomainException("El nombre del cliente es obligatorio.");
        if (string.IsNullOrWhiteSpace(customerPhone))
            throw new DomainException("El teléfono del cliente es obligatorio.");
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new DomainException("El servicio es obligatorio.");
        if (endAt <= startAt)
            throw new DomainException("La fecha de fin debe ser mayor a la fecha de inicio.");

        TenantId = tenantId;
        CustomerName = customerName.Trim();
        CustomerPhone = customerPhone.Trim();
        ServiceName = serviceName.Trim();
        StartAt = startAt;
        EndAt = endAt;
        StaffName = staffName?.Trim();
        Notes = notes?.Trim();
        ServiceProductId = serviceProductId;
        StaffUserId = staffUserId;
        Status = AppointmentStatus.Scheduled;
    }

    public Guid TenantId { get; private set; }
    public string CustomerName { get; private set; } = null!;
    public string CustomerPhone { get; private set; } = null!;
    public string ServiceName { get; private set; } = null!;
    public string? StaffName { get; private set; }
    public DateTimeOffset StartAt { get; private set; }
    public DateTimeOffset EndAt { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public string? Notes { get; private set; }
    /// <summary>The service of the catalog it books (its name is kept in <see cref="ServiceName"/>); null for older ones.</summary>
    public Guid? ServiceProductId { get; private set; }
    /// <summary>The team member who attends it (their name is kept in <see cref="StaffName"/>); null for older ones.</summary>
    public Guid? StaffUserId { get; private set; }

    /// <summary>
    /// Where it happens when it is at the customer's address (Fase 22E); none when the customer comes to the business.
    /// Always loaded with the appointment: the agenda, the travel time and its sale need it.
    /// </summary>
    public AppointmentVisit? Visit { get; private set; }
    /// <summary>The sale it was charged with (Fase 22E); none until it is charged.</summary>
    public Guid? SaleOrderId { get; private set; }
    /// <summary>Changes with every save, so two charges (or a charge and a change) saved at the same time collide.</summary>
    public byte[] Version { get; private set; } = null!;

    /// <summary>Who booked it (Fase 24D): the business's team, or the customer online.</summary>
    public AppointmentChannel Channel { get; private set; } = AppointmentChannel.Business;
    /// <summary>The customer account that booked it online (Fase 24D): it sees it in its appointments. Null otherwise.</summary>
    public Guid? CustomerUserId { get; private set; }
    /// <summary>
    /// The key the booking page generated for this request (Fase 24D): sent again, it answers the appointment already
    /// requested instead of a second one. Unique per business.
    /// </summary>
    public Guid? ClientRequestId { get; private set; }
    /// <summary>
    /// The link to follow and cancel the appointment without an account (Fase 24D), like an order's
    /// (<see cref="Order.TrackingTokenLength"/> URL-safe Base64 characters). Only for appointments booked online.
    /// </summary>
    public string? TrackingToken { get; private set; }
    /// <summary>When the customer cancelled it from the account or the link (Fase 24D); null otherwise.</summary>
    public DateTimeOffset? CancelledByCustomerAt { get; private set; }
    /// <summary>
    /// When the business accepted or declined it as a request online (Fase 29J, ADR 0064): how long its customer waited for
    /// an answer, which Explorar sums up. Null for one the team booked, one still waiting or cancelled by its customer first,
    /// one declined as fake (Fase 29V, ADR 0076: nobody waited for it, so it does not count), and every request answered
    /// before Fase 29J. Set once: a request is answered once.
    /// </summary>
    public DateTimeOffset? RespondedAt { get; private set; }
    /// <summary>
    /// The time its customer asked for when the request arrived online (Fase 29J, ADR 0064): its answer is due before it,
    /// even once the business moves the appointment to another time. Null for one the team booked and for requests from
    /// before Fase 29J.
    /// </summary>
    public DateTimeOffset? RequestedStartAt { get; private set; }
    /// <summary>
    /// When its business was reminded to answer it in time (Fase 29U, ADR 0075): once, an hour before its limit, while it
    /// still waited. Null for every other appointment, and for requests from before Fase 29U.
    /// </summary>
    public DateTimeOffset? AnswerReminderSentAt { get; private set; }
    /// <summary>
    /// When its business declined it as fake (Fase 29V, ADR 0076): a request online of a customer that nobody meant to book.
    /// While marked (<see cref="IsMarkedFake"/>) it does not count in how soon the business answers (<see cref="ResponseTimes"/>),
    /// and its phone may not request again in that business for <see cref="ResponseTimes.WindowDays"/> days. Null for every
    /// other appointment. Kept once the mark is taken back (<see cref="UnmarkedFakeAt"/>).
    /// </summary>
    public DateTimeOffset? MarkedFakeAt { get; private set; }
    /// <summary>When its business took back the mark of fake (Fase 29V, ADR 0076); null while marked, or never marked.</summary>
    public DateTimeOffset? UnmarkedFakeAt { get; private set; }
    /// <summary>Declined as fake, and the mark was not taken back (Fase 29V).</summary>
    public bool IsMarkedFake => MarkedFakeAt is not null && UnmarkedFakeAt is null;
    /// <summary>
    /// Where the customer gets the quote of a visit they requested online (Fase 29B, ADR 0053 point 11), when they gave it:
    /// one plain address (<see cref="InvoiceEmailAddress"/>). Null otherwise. Theirs: it never goes in the activity or a log.
    /// </summary>
    public string? CustomerEmail { get; private set; }
    /// <summary>
    /// The service order whose job it does (Fase 29F, ADR 0053 point 17): the job of an accepted quote, charged in that order,
    /// so the appointment never gets a sale of its own. Null for every other appointment.
    /// </summary>
    public Guid? WorkOrderId { get; private set; }
    /// <summary>
    /// A request online that came from the business's own team (Fase 29M, ADR 0067): the phone of one of its members or of
    /// the business, or someone signed in as a member. Explorar does not count how soon it was answered. Decided once, when
    /// it arrives; false for one the team booked and for every request from before Fase 29M.
    /// </summary>
    public bool FromTeam { get; private set; }
    /// <summary>
    /// The phone a request online arrived with, as the key of its customer (<see cref="MessagingPhone.ToKey"/>: +52 and ten
    /// digits for a Mexican mobile however it was written). Explorar counts one request per customer by it (Fase 29M, ADR
    /// 0067), so changing the appointment's phone later does not merge or split customers. Empty for one the team booked.
    /// </summary>
    public string RequestPhone { get; private set; } = string.Empty;

    public bool IsHomeVisit => Visit is not null;

    /// <summary>It does the job of a service order (<see cref="WorkOrderId"/>).</summary>
    public bool IsWork => WorkOrderId is not null;

    /// <summary>Booked online and waiting for the business to accept it.</summary>
    public bool IsRequest => Status == AppointmentStatus.Requested;

    public bool IsClosed => Status is AppointmentStatus.Cancelled or AppointmentStatus.Completed or AppointmentStatus.NoShow;

    public void Update(
        string customerName,
        string customerPhone,
        string serviceName,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        string? staffName,
        string? notes,
        Guid? serviceProductId = null,
        Guid? staffUserId = null)
    {
        EnsureOpen();
        if (string.IsNullOrWhiteSpace(customerName))
            throw new DomainException("El nombre del cliente es obligatorio.");
        if (string.IsNullOrWhiteSpace(customerPhone))
            throw new DomainException("El teléfono del cliente es obligatorio.");
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new DomainException("El servicio es obligatorio.");
        if (endAt <= startAt)
            throw new DomainException("La fecha de fin debe ser mayor a la fecha de inicio.");
        if (IsWork && endAt - startAt > AppointmentWorkRules.LongestAppointment)
            throw new DomainException(AppointmentWorkRules.TooLongMessage);

        CustomerName = customerName.Trim();
        CustomerPhone = customerPhone.Trim();
        ServiceName = serviceName.Trim();
        StartAt = startAt;
        EndAt = endAt;
        StaffName = staffName?.Trim();
        Notes = notes?.Trim();
        ServiceProductId = serviceProductId;
        StaffUserId = staffUserId;
    }

    public void ChangeStatus(AppointmentStatus newStatus)
    {
        EnsureOpen();
        if (newStatus == Status)
            return;
        // A request leaves its state only accepted or declined (or cancelled by its customer), and nothing goes back to it.
        if (Status == AppointmentStatus.Requested)
            throw new DomainException(OnlineBookingRules.DecideRequestFirstMessage);
        if (newStatus == AppointmentStatus.Requested)
            throw new DomainException(OnlineBookingRules.OnlyOnlineRequestedMessage);
        Status = newStatus;
    }

    /// <summary>
    /// A customer books online (Fase 24D, ADR 0025): the appointment holds its time and waits for the business to accept
    /// it. Always at the business: online booking arranges no visit. The phone is the one the booking page normalized.
    /// </summary>
    /// <param name="customerUserId">The customer account that booked it, when it was signed in.</param>
    /// <param name="clientRequestId">The booking page's key for this request.</param>
    /// <param name="trackingToken">The link to follow it without an account.</param>
    /// <param name="staffUserId">
    /// Who attends it, when the business says who attends online (Fase 24J): the person the customer chose, or the one the
    /// business's rule picked among the free ones; with <paramref name="staffName"/>, their name in the agenda.
    /// </param>
    /// <param name="fromTeam">It came from the business's own team (<see cref="FromTeam"/>).</param>
    public static Appointment RequestOnline(
        Guid tenantId,
        string customerName,
        string customerPhone,
        Guid serviceProductId,
        string serviceName,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        string? notes,
        Guid? customerUserId,
        Guid? clientRequestId,
        string trackingToken,
        Guid? staffUserId = null,
        string? staffName = null,
        bool fromTeam = false)
    {
        if (serviceProductId == Guid.Empty)
            throw new ArgumentException("The service is required.", nameof(serviceProductId));
        EnsureOnlineRequest(customerUserId, clientRequestId, trackingToken, staffUserId, staffName);

        var appointment = new Appointment(
            tenantId, customerName, customerPhone, serviceName, startAt, endAt,
            staffName: staffName, notes: string.IsNullOrWhiteSpace(notes) ? null : notes, serviceProductId: serviceProductId,
            staffUserId: staffUserId)
        {
            Status = AppointmentStatus.Requested,
            Channel = AppointmentChannel.Online,
            CustomerUserId = customerUserId,
            ClientRequestId = clientRequestId,
            TrackingToken = trackingToken,
            RequestedStartAt = startAt,
            FromTeam = fromTeam,
            RequestPhone = MessagingPhone.ToKey(customerPhone)
        };
        appointment.AddDomainEvent(new AppointmentRequestedEvent(appointment.Id, tenantId, HomeVisit: false, fromTeam));
        return appointment;
    }

    /// <summary>
    /// A customer requests a visit to quote online (Fase 29B, ADR 0053 points 11 and 12): the appointment holds its time
    /// «Por confirmar» like any online request, at the customer's address. The zone must reach its postal code (and be
    /// active); the visit keeps how the business charges it now, and what going there adds (nothing «Sin costo»).
    /// </summary>
    /// <param name="serviceProductId">The service the business picked as its visit; null when it has none.</param>
    /// <param name="serviceName">That service's name, or <see cref="HomeVisitRules.DefaultVisitName"/>.</param>
    /// <param name="problem">What happens, in the customer's words: the notes of the appointment.</param>
    /// <param name="customerEmail">Where the customer gets the quote, when they give it.</param>
    /// <param name="customerUserId">The customer account that requested it, when it was signed in.</param>
    /// <param name="clientRequestId">The request page's key for this request.</param>
    /// <param name="trackingToken">The link to follow it without an account.</param>
    /// <param name="staffUserId">Who goes, when the business says who attends online; with <paramref name="staffName"/>.</param>
    /// <param name="fromTeam">It came from the business's own team (<see cref="FromTeam"/>).</param>
    public static Appointment RequestHomeVisit(
        Guid tenantId,
        string customerName,
        string customerPhone,
        Guid? serviceProductId,
        string serviceName,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        string problem,
        string? customerEmail,
        DeliveryAddress address,
        DeliveryZone zone,
        HomeVisitChargePolicy chargePolicy,
        Guid? customerUserId,
        Guid? clientRequestId,
        string trackingToken,
        Guid? staffUserId = null,
        string? staffName = null,
        bool fromTeam = false)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(zone);
        if (serviceProductId == Guid.Empty)
            throw new ArgumentException("The service is required when given.", nameof(serviceProductId));
        if (!Enum.IsDefined(chargePolicy))
            throw new ArgumentOutOfRangeException(nameof(chargePolicy));
        EnsureOnlineRequest(customerUserId, clientRequestId, trackingToken, staffUserId, staffName);
        if (string.IsNullOrWhiteSpace(problem))
            throw new DomainException(HomeVisitRules.ProblemRequiredMessage);
        if (problem.Trim().Length > HomeVisitRules.MaxProblemLength)
            throw new DomainException(HomeVisitRules.ProblemTooLongMessage);
        if (zone.TenantId != tenantId || !zone.Covers(address.PostalCode))
            throw new DomainException(HomeVisitRules.NotCoveredMessage(address.PostalCode));

        var appointment = new Appointment(
            tenantId, customerName, customerPhone, serviceName, startAt, endAt,
            staffName: staffName, notes: problem, serviceProductId: serviceProductId, staffUserId: staffUserId)
        {
            Status = AppointmentStatus.Requested,
            Channel = AppointmentChannel.Online,
            CustomerUserId = customerUserId,
            ClientRequestId = clientRequestId,
            TrackingToken = trackingToken,
            CustomerEmail = InvoiceEmailAddress.Normalize(customerEmail),
            RequestedStartAt = startAt,
            FromTeam = fromTeam,
            RequestPhone = MessagingPhone.ToKey(customerPhone)
        };
        appointment.Visit = new AppointmentVisit(tenantId, appointment.Id, address, zone.Name, zone.Fee, chargePolicy);
        appointment.AddDomainEvent(new AppointmentRequestedEvent(appointment.Id, tenantId, HomeVisit: true, fromTeam));
        return appointment;
    }

    /// <summary>
    /// The business schedules the job of a service order that came from an accepted quote (Fase 29F, ADR 0053 point 17), or
    /// part of it: agendada like any appointment of the team. It happens at the place of the visit the quote came from, when
    /// there was one (<paramref name="at"/>, copied with nothing to charge: the job is charged in its order).
    /// </summary>
    /// <param name="workOrderId">The service order whose job it does.</param>
    /// <param name="at">The visit the quote came from, or none for a job at the business or not arranged yet.</param>
    public static Appointment ForWork(
        Guid tenantId,
        Guid workOrderId,
        string customerName,
        string customerPhone,
        string serviceName,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        AppointmentVisit? at,
        string? staffName = null,
        Guid? staffUserId = null,
        string? notes = null)
    {
        if (workOrderId == Guid.Empty)
            throw new ArgumentException("The order is required.", nameof(workOrderId));
        if (at is not null && at.TenantId != tenantId)
            throw new ArgumentException("The visit must be of the same business.", nameof(at));
        if (endAt - startAt > AppointmentWorkRules.LongestAppointment)
            throw new DomainException(AppointmentWorkRules.TooLongMessage);

        var appointment = new Appointment(
            tenantId, customerName, customerPhone, serviceName, startAt, endAt,
            staffName: staffName, notes: notes, staffUserId: staffUserId)
        {
            WorkOrderId = workOrderId
        };
        if (at is not null)
            appointment.Visit = AppointmentVisit.ForWork(appointment.Id, at);
        return appointment;
    }

    /// <summary>What every request a customer makes online carries: its account, its key, its link and who attends it.</summary>
    private static void EnsureOnlineRequest(
        Guid? customerUserId, Guid? clientRequestId, string trackingToken, Guid? staffUserId, string? staffName)
    {
        if (customerUserId == Guid.Empty)
            throw new ArgumentException("The account is required when given.", nameof(customerUserId));
        if (clientRequestId == Guid.Empty)
            throw new ArgumentException("The key is required when given.", nameof(clientRequestId));
        if (!Order.IsTrackingToken(trackingToken))
            throw new ArgumentException("The link must look like one.", nameof(trackingToken));
        if (staffUserId == Guid.Empty || (staffUserId is null) != string.IsNullOrWhiteSpace(staffName))
            throw new ArgumentException("Who attends it goes with their name.", nameof(staffUserId));
    }

    /// <summary>
    /// The business accepts a request: it is agendada. Also once its time began, with the customer already there; once it
    /// ended, it is moved to another time first.
    /// </summary>
    public void Accept(DateTimeOffset now)
    {
        if (Status != AppointmentStatus.Requested)
            throw new DomainException(OnlineBookingRules.NotRequestedMessage);
        if (EndAt <= now)
            throw new DomainException(OnlineBookingRules.RequestTimePassedMessage);
        Status = AppointmentStatus.Scheduled;
        RespondedAt = now;
    }

    /// <summary>
    /// Reminds its business to answer a request online still waiting (Fase 29U, ADR 0075): once, from
    /// <see cref="ResponseTimes.RemindAt"/> until its limit (<see cref="ResponseTimes.AnswerBy"/>). Never one of its own team,
    /// one whose limit came less than two hours after it arrived, or one already answered, cancelled or reminded:
    /// <see cref="AppointmentAnswerDueSoonEvent"/> then is not raised.
    /// </summary>
    /// <returns>Whether it is reminded now.</returns>
    public bool RemindToAnswer(DateTimeOffset now)
    {
        if (Channel != AppointmentChannel.Online || FromTeam || Status != AppointmentStatus.Requested || AnswerReminderSentAt is not null)
            return false;
        var askedFor = RequestedStartAt ?? StartAt;
        if (ResponseTimes.RemindAt(CreatedAt, askedFor) is not { } remindAt
            || now < remindAt
            || now >= ResponseTimes.AnswerBy(CreatedAt, askedFor))
            return false;

        AnswerReminderSentAt = now;
        AddDomainEvent(new AppointmentAnswerDueSoonEvent(Id, TenantId));
        return true;
    }

    /// <summary>The business declines a request: it is cancelled and its time is free again.</summary>
    public void Decline(DateTimeOffset now)
    {
        if (Status != AppointmentStatus.Requested)
            throw new DomainException(OnlineBookingRules.NotRequestedMessage);
        Status = AppointmentStatus.Cancelled;
        RespondedAt = now;
    }

    /// <summary>
    /// The business declines a request online of a customer as fake (Fase 29V, ADR 0076): it is cancelled and its time is
    /// free, as when declined, and <see cref="MarkedFakeAt"/> keeps when. It keeps no <see cref="RespondedAt"/>: nobody
    /// waited for an answer, so it does not count in how soon the business answers. Also past its time or its limit. One of
    /// its own team does not count anyway.
    /// </summary>
    public void MarkFake(DateTimeOffset now)
    {
        if (Channel != AppointmentChannel.Online || FromTeam)
            throw new DomainException(OnlineBookingRules.FakeOnlyFromCustomersMessage);
        if (Status != AppointmentStatus.Requested)
            throw new DomainException(OnlineBookingRules.NotRequestedMessage);
        Status = AppointmentStatus.Cancelled;
        MarkedFakeAt = now;
    }

    /// <summary>
    /// The business takes back the mark of a request it declined as fake (Fase 29V, ADR 0076): it was a customer's after all.
    /// It stays declined, now: it counts again as answered when the mark is taken back, never as a quick answer its customer
    /// did not get, and its phone may request again. When it was marked stays kept. Once taken back it is not marked again.
    /// </summary>
    public void UnmarkFake(DateTimeOffset now)
    {
        if (!IsMarkedFake)
            throw new DomainException(OnlineBookingRules.NotMarkedFakeMessage);
        UnmarkedFakeAt = now;
        RespondedAt = now;
    }

    /// <summary>
    /// Its customer cancels an appointment booked online, from the account or the link, before it starts: requested,
    /// agendada or confirmed. Once in service, or closed, only the business changes it. The business hears of it (Fase 29O,
    /// ADR 0069): <see cref="AppointmentCancelledByCustomerEvent"/>.
    /// </summary>
    public void CancelByCustomer(DateTimeOffset now)
    {
        if (Channel != AppointmentChannel.Online)
            throw new DomainException(OnlineBookingRules.CustomerCancelNotOnlineMessage);
        if (IsClosed)
            throw new DomainException(OnlineBookingRules.CustomerCancelClosedMessage);
        if (Status is not (AppointmentStatus.Requested or AppointmentStatus.Scheduled or AppointmentStatus.Confirmed)
            || StartAt <= now)
            throw new DomainException(OnlineBookingRules.CustomerCancelStartedMessage);

        var wasRequest = Status == AppointmentStatus.Requested;
        Status = AppointmentStatus.Cancelled;
        CancelledByCustomerAt = now;
        AddDomainEvent(new AppointmentCancelledByCustomerEvent(Id, TenantId, IsHomeVisit, FromTeam, wasRequest));
    }

    /// <summary>
    /// The appointment happens at the customer's address: the zone must reach its postal code (and be active), and the
    /// visit charges what the zone charges to go there (its minimum and «envío gratis desde» are for orders). At the same
    /// address it keeps the zone and the charge it was arranged with: what the customer agreed to. The job of an order
    /// charges nothing to go there: it is charged in its order (Fase 29F).
    /// </summary>
    /// <returns>False when it was already at that address.</returns>
    public bool ArrangeVisit(DeliveryAddress address, DeliveryZone zone, DateTimeOffset changedAt)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(zone);
        EnsureOpen();
        if (Visit is not null && Visit.IsAt(address))
            return UpdateVisitReferences(address, changedAt);
        if (zone.TenantId != TenantId || !zone.Covers(address.PostalCode))
            throw new DomainException(HomeVisitRules.NotCoveredMessage(address.PostalCode));

        var fee = IsWork ? 0m : zone.Fee;
        if (Visit is null)
            Visit = new AppointmentVisit(TenantId, Id, address, zone.Name, fee);
        else
            Visit.MoveTo(address, zone.Name, fee);
        // The appointment changes with its visit, so its version catches a charge (or a cancellation) saved meanwhile.
        UpdatedAt = changedAt;
        return true;
    }

    /// <summary>
    /// The same place with other references («timbre 6»): the zone and the charge agreed stay, even if the zone no longer
    /// reaches the postal code, and nothing is quoted again.
    /// </summary>
    /// <returns>False when they were the same.</returns>
    public bool UpdateVisitReferences(DeliveryAddress address, DateTimeOffset changedAt)
    {
        ArgumentNullException.ThrowIfNull(address);
        EnsureOpen();
        if (Visit is null || !Visit.IsAt(address))
            throw new InvalidOperationException("Only the references of the place already arranged change without a zone.");
        if (!Visit.ChangeReferences(address.References))
            return false;

        UpdatedAt = changedAt;
        return true;
    }

    /// <summary>The customer comes to the business after all: the visit, and its charge, are dropped.</summary>
    /// <returns>False when it was not a visit.</returns>
    public bool MoveToBusiness(DateTimeOffset changedAt)
    {
        EnsureOpen();
        if (Visit is null)
            return false;

        Visit = null;
        UpdatedAt = changedAt;
        return true;
    }

    /// <summary>
    /// The appointment was charged with <paramref name="orderId"/>: it is done, if it was still open. A cancelled
    /// appointment, or one the customer missed, is not charged; one already charged only again when its sale was cancelled.
    /// The job of an order is charged in that order, never with a sale of its own.
    /// </summary>
    public void RecordSale(Guid orderId, bool previousSaleCancelled = false)
    {
        if (orderId == Guid.Empty)
            throw new ArgumentException("The sale is required.", nameof(orderId));
        if (IsWork)
            throw new DomainException(AppointmentWorkRules.ChargedInItsOrderMessage);
        if (Status == AppointmentStatus.Cancelled)
            throw new DomainException(HomeVisitRules.CancelledSaleMessage);
        if (Status == AppointmentStatus.NoShow)
            throw new DomainException(HomeVisitRules.NoShowSaleMessage);
        if (Status == AppointmentStatus.Requested)
            throw new DomainException(OnlineBookingRules.AcceptBeforeChargingMessage);
        if (SaleOrderId is not null && !previousSaleCancelled)
            throw new DomainException(HomeVisitRules.AlreadySoldMessage);

        SaleOrderId = orderId;
        Status = AppointmentStatus.Completed;
    }

    /// <summary>
    /// Why this visit takes no more photos of what happens from its link, or null when it takes one more (Fase 29H, ADR 0062):
    /// see <see cref="HomeVisitRules.ProblemPhotoRefusal"/>. Read before the file, so a refused photo costs nothing.
    /// </summary>
    /// <param name="photosSoFar">How many it already has.</param>
    public string? ProblemPhotoRefusal(int photosSoFar, DateTimeOffset now)
        => HomeVisitRules.ProblemPhotoRefusal(Channel, IsHomeVisit, IsWork, Status, StartAt, photosSoFar, now);

    /// <summary>
    /// The customer sends a photo of what happens from the link of the visit they requested (Fase 29H, ADR 0062). It takes the
    /// next place among its photos; it changes nothing of the appointment, so it never conflicts with the business deciding
    /// the request. The base keeps each place once, so two photos sent at once never make one more than allowed.
    /// </summary>
    /// <param name="photosSoFar">How many it already has.</param>
    /// <param name="fileKey">The key of the private file already saved for this business.</param>
    public AppointmentProblemPhoto AddProblemPhoto(int photosSoFar, string fileKey, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(fileKey) || fileKey.Length > AppointmentProblemPhoto.MaxFileKeyLength)
            throw new ArgumentException("The key of the file is required and short.", nameof(fileKey));
        if (ProblemPhotoRefusal(photosSoFar, now) is { } refusal)
            throw new DomainException(refusal);

        return new AppointmentProblemPhoto(TenantId, Id, photosSoFar, fileKey, now);
    }

    private void EnsureOpen()
    {
        if (IsClosed)
            throw new DomainException("La cita ya está cerrada y no permite más cambios.");
    }
}
