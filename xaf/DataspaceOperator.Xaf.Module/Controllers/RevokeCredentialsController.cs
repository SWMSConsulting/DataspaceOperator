using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.Persistent.Base;
using Microsoft.Extensions.DependencyInjection;
using DataspaceOperator.Core.Protocol;
using DataspaceOperator.Xaf.Module.BusinessObjects;

namespace DataspaceOperator.Xaf.Module.Controllers;

/// <summary>
/// Operator action: revoke every credential of a participant, and do the same automatically when a
/// participant is deleted.
///
/// Why this exists: deleting a participant removes them from the BDRS directory and stops further
/// issuance, but it does NOT touch the credentials already sitting in their wallet. Those stay
/// signed, unexpired and — because DCP has no way to ask the issuer at verification time — are
/// still honoured by every other connector. The status list is the only channel that reaches them,
/// so revocation has to be triggered explicitly.
///
/// Revocation only has an effect if issued credentials actually carry a <c>credentialStatus</c>
/// (configuration <c>Issuer:IncludeCredentialStatus</c>). With it off, the bit is set but nothing
/// ever looks at it.
/// </summary>
public class RevokeCredentialsController : ViewController
{
    public RevokeCredentialsController()
    {
        TargetObjectType = typeof(ParticipantEntity);
        var action = new SimpleAction(this, "RevokeParticipantCredentials", PredefinedCategory.RecordEdit)
        {
            Caption = "Revoke All Credentials",
            SelectionDependencyType = SelectionDependencyType.RequireSingleObject,
            ConfirmationMessage =
                "Revoke ALL credentials of this participant? Other connectors stop accepting them " +
                "once they refresh the status list. This cannot be undone.",
            ImageName = "Action_Deny",
        };
        action.Execute += Action_Execute;
    }

    protected override void OnActivated()
    {
        base.OnActivated();
        ObjectSpace.ObjectDeleting += ObjectSpace_ObjectDeleting;
    }

    protected override void OnDeactivated()
    {
        ObjectSpace.ObjectDeleting -= ObjectSpace_ObjectDeleting;
        base.OnDeactivated();
    }

    /// <summary>
    /// Deleting a participant without revoking would leave them with working credentials, so the
    /// deletion is paired with a revocation. Deliberately best-effort: a failure here must not block
    /// the delete, but it is surfaced so it cannot pass unnoticed.
    /// </summary>
    private void ObjectSpace_ObjectDeleting(object sender, ObjectsManipulatingEventArgs e)
    {
        foreach (var obj in e.Objects)
        {
            if (obj is not ParticipantEntity p || string.IsNullOrEmpty(p.Did)) continue;
            try
            {
                var count = Revoke(p.Did);
                if (count > 0)
                    Application.ShowViewStrategy.ShowMessage(
                        $"Revoked {count} credential(s) of {p.Name} before deletion.",
                        InformationType.Info);
            }
            catch (Exception ex)
            {
                Application.ShowViewStrategy.ShowMessage(
                    $"WARNING: could not revoke credentials of {p.Name} ({ex.GetBaseException().Message}). " +
                    "They remain valid at other connectors until they expire.",
                    InformationType.Error);
            }
        }
    }

    private void Action_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        var participant = (ParticipantEntity)e.CurrentObject;
        if (string.IsNullOrEmpty(participant.Did))
        {
            Application.ShowViewStrategy.ShowMessage("Participant has no DID.", InformationType.Warning);
            return;
        }

        try
        {
            var count = Revoke(participant.Did);
            Application.ShowViewStrategy.ShowMessage(
                count == 0
                    ? $"{participant.Name} had no live credentials to revoke."
                    : $"Revoked {count} credential(s) of {participant.Name}.",
                count == 0 ? InformationType.Info : InformationType.Success);
            ObjectSpace.Refresh();
        }
        catch (Exception ex)
        {
            Application.ShowViewStrategy.ShowMessage(
                $"Revocation failed: {ex.GetBaseException().Message}", InformationType.Error);
        }
    }

    /// <summary>
    /// Runs on the thread pool for the same reason as the issuance action: a sync wait on the Blazor
    /// circuit's synchronization context deadlocks.
    /// </summary>
    private int Revoke(string did)
    {
        var appServices = Application.ServiceProvider;
        return Task.Run(async () =>
        {
            using var scope = appServices.CreateScope();
            var issuance = scope.ServiceProvider.GetRequiredService<DcpIssuanceService>();
            return await issuance.RevokeAllForHolderAsync(did);
        }).GetAwaiter().GetResult();
    }
}
