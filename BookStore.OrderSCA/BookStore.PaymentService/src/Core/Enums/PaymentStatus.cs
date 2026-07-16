namespace BookStore.PaymentService.Core.Enums;

/// <summary>
/// Payment lifecycle. A charge attempt lands in exactly one terminal state — <see cref="Captured"/>
/// (gateway approved) or <see cref="Failed"/> (declined/errored). <see cref="Pending"/> exists for a
/// record created before the gateway result is known (not used on the synchronous-confirm path today,
/// reserved for the async-webhook path — see docs/ROADMAP.md).
/// </summary>
public enum PaymentStatus
{
    Pending = 0,
    Captured = 1,
    Failed = 2
}
