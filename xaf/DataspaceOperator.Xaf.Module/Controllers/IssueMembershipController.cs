using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.Persistent.Base;
using Microsoft.Extensions.DependencyInjection;
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

        using var scope = Application.ServiceProvider.CreateScope();
        var issuance = scope.ServiceProvider.GetRequiredService<DcpIssuanceService>();
        var result = issuance.IssueAsync(participant.Did, "MembershipCredential").GetAwaiter().GetResult();

        ObjectSpace.Refresh();
        Application.ShowViewStrategy.ShowMessage(
            $"Issued {result.CredentialType} for {participant.Name} (id {result.Id}).",
            InformationType.Success);
    }
}
