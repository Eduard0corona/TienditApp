namespace FoodParkDigital.Application.DeliveryPlatforms;

/// <summary>What a folio of a platform's statement is in TienditApp (Fase 22H, ADR 0040).</summary>
public enum PlatformStatementMatchStatus
{
    /// <summary>Its order is already in this settlement.</summary>
    InThisSettlement = 1,

    /// <summary>Its order is pending and registered by the end of the period: the settlement can take it.</summary>
    Pending = 2,

    /// <summary>Its order is pending but was registered after the period: a later settlement takes it.</summary>
    AfterPeriod = 3,

    /// <summary>Its order is already in another settlement in force.</summary>
    Settled = 4,

    /// <summary>Its order brings nothing: given back whole, or not collected yet by the business.</summary>
    NothingExpected = 5,

    /// <summary>Several pending orders have the folio (Uber Eats and DiDi Food repeat them): the person picks.</summary>
    Ambiguous = 6,

    /// <summary>No order of the platform has the folio.</summary>
    Unknown = 7
}

/// <summary>A platform's statement against its orders, line by line, without saving anything.</summary>
public class PlatformStatementMatchDto
{
    /// <summary>One per folio, in the order its first line came in the statement.</summary>
    public List<PlatformStatementFolioDto> Folios { get; set; } = [];

    /// <summary>The lines without a folio, by concept, in the order the first line of each came.</summary>
    public List<PlatformStatementConceptDto> Concepts { get; set; } = [];

    /// <summary>The lines whose folio cannot be a platform's folio.</summary>
    public List<PlatformStatementUnreadableDto> Unreadable { get; set; } = [];

    /// <summary>What every line of the statement adds up to.</summary>
    public decimal StatementTotal { get; set; }

    /// <summary>What the statement says of the orders the settlement takes (in it or pending).</summary>
    public decimal MatchedTotal { get; set; }

    /// <summary>What TienditApp expects of those same orders.</summary>
    public decimal ExpectedTotal { get; set; }

    /// <summary>What the lines without a folio add up to.</summary>
    public decimal ConceptsTotal { get; set; }

    /// <summary>What the folios this settlement cannot take add up to, and the lines whose folio cannot be read.</summary>
    public decimal UnmatchedTotal { get; set; }
}

/// <summary>A folio of the statement, its lines added up, and what it is in TienditApp.</summary>
public class PlatformStatementFolioDto
{
    /// <summary>The folio, normalized as the orders keep it.</summary>
    public string Folio { get; set; } = null!;

    /// <summary>The lines of the file it came in (from 1).</summary>
    public List<int> Rows { get; set; } = [];

    /// <summary>What its lines add up to in the statement.</summary>
    public decimal StatementAmount { get; set; }

    public PlatformStatementMatchStatus Status { get; set; }

    /// <summary>
    /// The order it is (in this settlement or pending), the orders it could be (ambiguous), or the most recent one that
    /// explains why the settlement cannot take it.
    /// </summary>
    public List<PlatformSettlementOrderDto> Orders { get; set; } = [];

    /// <summary>What TienditApp expects of its order, when it is one the settlement takes.</summary>
    public decimal? ExpectedNet { get; set; }

    /// <summary>The statement minus what TienditApp expects, when it is one the settlement takes.</summary>
    public decimal? Difference { get; set; }

    /// <summary>The settlement in force that already has its order (<see cref="PlatformStatementMatchStatus.Settled"/>).</summary>
    public Guid? SettlementId { get; set; }
}

/// <summary>The lines of the statement without a folio that share a concept, added up.</summary>
public class PlatformStatementConceptDto
{
    /// <summary>The concept as the file wrote it, trimmed; empty when the file did not say.</summary>
    public string Concept { get; set; } = "";

    public List<int> Rows { get; set; } = [];
    public decimal Amount { get; set; }
}

/// <summary>A line of the statement whose folio cannot be a platform's folio.</summary>
public class PlatformStatementUnreadableDto
{
    public int Row { get; set; }

    /// <summary>The folio as the file wrote it.</summary>
    public string Folio { get; set; } = null!;

    public string? Concept { get; set; }
    public decimal Amount { get; set; }
}
