using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.Persistent.Base;
using Microsoft.Extensions.DependencyInjection;
using DataspaceOperator.Core.Abstractions;
using DataspaceOperator.Core.Protocol;
using DataspaceOperator.Xaf.Module.BusinessObjects;

namespace DataspaceOperator.Xaf.Module.Controllers;

/// <summary>
/// Operator action: issue a credential to the selected participant - one of the supported types, or
/// all of them. DSP 2025-1 asks every counterparty for Membership, Bpn and DataExchangeGovernance
/// credentials at once; a participant holding only the membership gets 401 on every call, so "All"
/// is the normal onboarding choice.
///
/// Issuance is holder-initiated: we send a CredentialOffer to the participant's wallet, which then
/// requests the credential from our IssuerService, and we deliver a correlated CredentialMessage.
/// </summary>
public class IssueCredentialController : ViewController
{
    private const string AllTypes = "All";

    /// <summary>How long to wait for the wallet to request and receive one offered credential.</summary>
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(30);

    public IssueCredentialController()
    {
        TargetObjectType = typeof(ParticipantEntity);
        var action = new SingleChoiceAction(this, "IssueCredential", PredefinedCategory.RecordEdit)
        {
            Caption = "Issue Credential",
            ItemType = SingleChoiceActionItemType.ItemIsOperation,
            SelectionDependencyType = SelectionDependencyType.RequireSingleObject,
            ConfirmationMessage = "Issue the selected credential(s) to this participant?",
            ImageName = "Action_Grant",
        };
        action.Items.Add(new ChoiceActionItem(AllTypes, "All (required for DSP 2025-1)", AllTypes));
        foreach (var type in IssuerMetadata.SupportedTypes)
            action.Items.Add(new ChoiceActionItem(type, type, type));
        action.Execute += Action_Execute;
    }

    private void Action_Execute(object sender, SingleChoiceActionExecuteEventArgs e)
    {
        var participant = (ParticipantEntity)e.CurrentObject;
        if (string.IsNullOrEmpty(participant.Did))
        {
            Application.ShowViewStrategy.ShowMessage("Participant has no DID.", InformationType.Warning);
            return;
        }

        var choice = (string)e.SelectedChoiceActionItem.Data;
        var types = choice == AllTypes ? IssuerMetadata.SupportedTypes : [choice];
        var appServices = Application.ServiceProvider;
        var did = participant.Did;
        try
        {
            // Offloaded to the thread pool to avoid a sync-over-async deadlock on the Blazor circuit.
            var (issued, error) = Task.Run(() => IssueAsync(appServices, did, types)).GetAwaiter().GetResult();

            if (error is null)
                Application.ShowViewStrategy.ShowMessage(types.Length == 1
                    ? $"Sent {types[0]} offer to {participant.Name}'s wallet. The wallet will request " +
                      "and store the credential via DCP."
                    : $"Issued {string.Join(", ", issued)} to {participant.Name}.", InformationType.Success);
            else
                Application.ShowViewStrategy.ShowMessage(issued.Count == 0
                    ? error
                    : $"Issued {string.Join(", ", issued)}; then {error}", InformationType.Error);
        }
        catch (Exception ex)
        {
            Application.ShowViewStrategy.ShowMessage(
                $"Issuance failed: {ex.GetBaseException().Message}", InformationType.Error);
        }
    }

    /// <summary>
    /// Offer the types one after another. A single type returns once the offer is accepted. For
    /// several, each must be requested and delivered before the next offer goes out: the issuer maps
    /// the wallet's request (which names no type) to the last offer sent to that holder.
    /// </summary>
    private static async Task<(List<string> Issued, string? Error)> IssueAsync(
        IServiceProvider appServices, string did, string[] types)
    {
        using var scope = appServices.CreateScope();
        var offers = scope.ServiceProvider.GetRequiredService<ICredentialOfferService>();
        var tracker = scope.ServiceProvider.GetRequiredService<IssuanceRequestTracker>();

        var issued = new List<string>();
        foreach (var type in types)
        {
            var since = DateTimeOffset.UtcNow;
            var result = await offers.SendOfferAsync(did, type);
            if (!result.Success)
                return (issued, $"{type} offer failed: {result.Error}");
            if (types.Length == 1)
                return ([type], null);

            var settled = await tracker.WaitForSettledAsync(did, type, since, SettleTimeout);
            if (settled is null)
                return (issued, $"the wallet did not request {type} within {SettleTimeout.TotalSeconds:0} s.");
            if (settled.State == IssuanceRequestTracker.RequestState.Rejected)
                return (issued, $"{type} issuance failed: {settled.Error}");
            issued.Add(type);
        }
        return (issued, null);
    }
}
