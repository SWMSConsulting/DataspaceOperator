using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.Persistent.Base;
using Microsoft.Extensions.DependencyInjection;
using DataspaceOperator.Core.Domain;
using DataspaceOperator.Core.Protocol;
using DataspaceOperator.Xaf.Module.BusinessObjects;

namespace DataspaceOperator.Xaf.Module.Controllers;

/// <summary>
/// Operator action: issue a MembershipCredential to the selected participant.
/// This is the "issuer-initiated" issuance from the operator UI — it signs a JWT-VC and records
/// it as an IssuedCredential (visible in the app).
/// </summary>
public class IssueMembershipController : ViewController
{
    public IssueMembershipController()
    {
        TargetObjectType = typeof(ParticipantEntity);
        var action = new SimpleAction(this, "IssueMembershipCredential", PredefinedCategory.RecordEdit)
        {
            Caption = "Issue Membership Credential",
            SelectionDependencyType = SelectionDependencyType.RequireSingleObject,
            ConfirmationMessage = "Issue a MembershipCredential for the selected participant?",
            ImageName = "Action_Grant",
        };
        action.Execute += Action_Execute;
    }

    private void Action_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        var participant = (ParticipantEntity)e.CurrentObject;
        if (string.IsNullOrEmpty(participant.Did))
        {
            Application.ShowViewStrategy.ShowMessage("Participant has no DID.", InformationType.Warning);
            return;
        }

        var appServices = Application.ServiceProvider;
        var did = participant.Did;
        try
        {
            // Offload to the thread pool: avoids a sync-over-async deadlock on the Blazor circuit
            // (issuance awaits HTTP delivery + EF). The scope is kept alive until issuance completes.
            var result = Task.Run(async () =>
            {
                using var scope = appServices.CreateScope();
                var issuance = scope.ServiceProvider.GetRequiredService<DcpIssuanceService>();
                return await issuance.IssueAsync(did, "MembershipCredential");
            }).GetAwaiter().GetResult();

            ObjectSpace.Refresh();

            // Delivery is best-effort: the holder's own wallet may be unreachable from here.
            var note = result.Delivery == DeliveryStatus.Delivered
                ? "delivered to the holder's wallet"
                : $"stored (delivery: {result.Delivery} — holder wallet not reachable)";
            Application.ShowViewStrategy.ShowMessage(
                $"Issued {result.CredentialType} for {participant.Name}; {note}.",
                InformationType.Success);
        }
        catch (Exception ex)
        {
            Application.ShowViewStrategy.ShowMessage(
                $"Issuance failed: {ex.GetBaseException().Message}", InformationType.Error);
        }
    }
}
