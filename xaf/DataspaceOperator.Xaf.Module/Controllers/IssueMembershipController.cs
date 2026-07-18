using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.Persistent.Base;
using Microsoft.Extensions.DependencyInjection;
using DataspaceOperator.Core.Abstractions;
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
            // Real DCP issuance is holder-initiated: we send a CredentialOffer to the participant's
            // wallet, which then auto-requests the credential from our IssuerService, and we deliver
            // a correlated CredentialMessage. Offloaded to the thread pool to avoid a sync-over-async
            // deadlock on the Blazor circuit.
            var result = Task.Run(async () =>
            {
                using var scope = appServices.CreateScope();
                var offers = scope.ServiceProvider.GetRequiredService<ICredentialOfferService>();
                return await offers.SendOfferAsync(did, "MembershipCredential");
            }).GetAwaiter().GetResult();

            if (result.Success)
                Application.ShowViewStrategy.ShowMessage(
                    $"Sent MembershipCredential offer to {participant.Name}'s wallet. The wallet will " +
                    "request and store the credential via DCP.", InformationType.Success);
            else
                Application.ShowViewStrategy.ShowMessage(
                    $"Offer failed: {result.Error}", InformationType.Error);
        }
        catch (Exception ex)
        {
            Application.ShowViewStrategy.ShowMessage(
                $"Issuance failed: {ex.GetBaseException().Message}", InformationType.Error);
        }
    }
}
