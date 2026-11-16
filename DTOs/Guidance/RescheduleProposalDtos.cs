using System.ComponentModel.DataAnnotations;

namespace CDM_OneServe_API.DTOs.Guidance;

public class ProposeRescheduleRequest : RescheduleAppointmentRequest
{
    // Required when responding to an existing proposal; prevents stale responses.
    public int? PreviousProposalId { get; set; }
}

public class AcceptRescheduleRequest
{
    [Range(1, int.MaxValue)] public int ProposalId { get; set; }
}
