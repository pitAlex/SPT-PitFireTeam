namespace pitTeam.Server.Models;

// Raid evidence and its one-time settlement reservation, stored separately from active policies.
public sealed class FollowerInsuranceRaidDiagnostic
{
    public string RaidId { get; set; } = Guid.NewGuid().ToString("N");
    public string ServerId { get; set; } = string.Empty;
    public string TransitionId { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    // Legacy diagnostic raids predate claim authorization and must never be replayed as rewards.
    public bool SettlementEligible { get; set; }
    public bool EndReceived { get; set; }
    public bool AwaitingTransit { get; set; }
    public bool IncompleteTransit { get; set; }
    public bool PlayerInventoryKnown { get; set; }
    public bool ReportsComplete { get; set; }
    public bool ReportsFailed { get; set; }
    public List<string> ExpectedReportIds { get; set; } = [];
    public List<string> ReceivedReportIds { get; set; } = [];
    public List<string> PlayerItemIds { get; set; } = [];
    public List<string> CourierItemIds { get; set; } = [];
    public List<string> TransferItemIds { get; set; } = [];
    public List<FollowerInsuranceRaidParticipant> Participants { get; set; } = [];
    public List<FollowerInsuranceRaidItem> InsuredItems { get; set; } = [];
    public int Revision { get; set; }
    public string LastReportSignature { get; set; } = string.Empty;
    public List<FollowerInsuranceRaidFinding> Findings { get; set; } = [];
    // Reserved before adding retained follower policies to the PMC; uncertain writes are not replayed.
    public string PlayerPolicyTransferState { get; set; } = string.Empty;
    public List<string> PlayerPolicyTransferItemIds { get; set; } = [];
    // Persisted before touching SPT's profile. An uncertain reservation is never retried automatically.
    public string SettlementState { get; set; } = string.Empty;
    public List<string> SettlementItemIds { get; set; } = [];
}

public sealed class FollowerInsuranceRaidParticipant
{
    public string Aid { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string InitialEquipmentJson { get; set; } = string.Empty;
    public bool OutcomeReceived { get; set; }
    public bool Escaped { get; set; }
    public bool EquipmentSnapshotKnown { get; set; }
    public List<string> SavedItemIds { get; set; } = [];
    public List<string> EscapedItemIds { get; set; } = [];
}

public sealed record FollowerInsuranceRaidItem(string ItemId, string TemplateId, string TraderId,
    string OwnerAid, string ParentId, string SlotId);

public sealed record FollowerInsuranceRaidFinding(string ItemId, string TraderId, string OwnerAid,
    string Status, string Reason);
