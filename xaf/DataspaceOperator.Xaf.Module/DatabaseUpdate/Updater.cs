using DevExpress.ExpressApp;
using DevExpress.Data.Filtering;
using DevExpress.Persistent.Base;
using DevExpress.ExpressApp.Updating;
using DevExpress.ExpressApp.EF;
using DevExpress.Persistent.BaseImpl.EF;
using Microsoft.Extensions.DependencyInjection;
using DataspaceOperator.Xaf.Module.BusinessObjects;
using DataspaceOperator.Core.Domain;

namespace DataspaceOperator.Xaf.Module.DatabaseUpdate;

// For more typical usage scenarios, be sure to check out https://docs.devexpress.com/eXpressAppFramework/DevExpress.ExpressApp.Updating.ModuleUpdater
public class Updater : ModuleUpdater {
    public Updater(IObjectSpace objectSpace, Version currentDBVersion) :
        base(objectSpace, currentDBVersion) {
    }
    public override void UpdateDatabaseAfterUpdateSchema() {
        base.UpdateDatabaseAfterUpdateSchema();

        // Seed governance: trust our own issuer for all credential types.
        const string ownIssuerDid = "did:web:issuer.localhost";
        // Known credential type names (pick list for a trusted issuer's SupportedTypes).
        SeedType("MembershipCredential");
        SeedType("DataExchangeGovernanceCredential");
        SeedType("BpnCredential");

        var own = ObjectSpace.FirstOrDefault<TrustedIssuerEntity>(x => x.Did == ownIssuerDid);
        if(own == null) {
            own = ObjectSpace.CreateObject<TrustedIssuerEntity>();
            own.Did = ownIssuerDid;
            own.IsOwnIssuer = true;   // empty SupportedTypes = trusted for all types
            ObjectSpace.CommitChanges();
        }

        void SeedType(string name) {
            if(ObjectSpace.FirstOrDefault<CredentialTypeEntity>(x => x.Name == name) == null) {
                var t = ObjectSpace.CreateObject<CredentialTypeEntity>();
                t.Name = name;
            }
        }

        // Seed issuable credential definitions (editable templates; add more types via the UI).
        SeedDefinition("MembershipCredential", null,
            "{\"holderIdentifier\":\"{bpn}\",\"memberOf\":\"Dataspace\",\"membershipType\":\"FullMember\",\"since\":\"{now}\"}");
        SeedDefinition("DataExchangeGovernanceCredential", "https://w3id.org/catenax/credentials/v1.0.0",
            "{\"holderIdentifier\":\"{bpn}\",\"contractVersion\":\"1.0.0\",\"contractTemplate\":\"https://public.example.org/contracts/DataExchangeGovernance.v1.pdf\"}");
        SeedDefinition("BpnCredential", "https://w3id.org/catenax/credentials/v1.0.0",
            "{\"bpn\":\"{bpn}\",\"holderIdentifier\":\"{bpn}\"}");

        // Seed example participants from the MXD sample (Alice & Bob).
        SeedParticipant("Alice GmbH", "BPNL000000000001", "did:web:alice-ih%3A7083:alice");
        SeedParticipant("Bob AG", "BPNL000000000002", "did:web:bob-ih%3A7083:bob");
        ObjectSpace.CommitChanges();

        void SeedDefinition(string type, string? contextUrl, string template) {
            var d = ObjectSpace.FirstOrDefault<CredentialDefinitionEntity>(x => x.CredentialType == type);
            if(d == null) {
                d = ObjectSpace.CreateObject<CredentialDefinitionEntity>();
                d.CredentialType = type;
                d.ContextUrl = contextUrl;
                d.ClaimTemplateJson = template;
                d.ValiditySeconds = 31_536_000;
            }
        }

        void SeedParticipant(string name, string bpn, string did) {
            var p = ObjectSpace.FirstOrDefault<ParticipantEntity>(x => x.Did == did);
            if(p == null) {
                p = ObjectSpace.CreateObject<ParticipantEntity>();
                p.Name = name;
                p.Bpn = bpn;          // BPN↔DID live on the participant; BDRS projects over it
                p.Did = did;
                p.State = ParticipantState.Active;
                p.OnboardedUtc = DateTime.UtcNow;
            }
        }

        //string name = "MyName";
        //EntityObject1 theObject = ObjectSpace.FirstOrDefault<EntityObject1>(u => u.Name == name);
        //if(theObject == null) {
        //    theObject = ObjectSpace.CreateObject<EntityObject1>();
        //    theObject.Name = name;
        //}

        //ObjectSpace.CommitChanges(); //Uncomment this line to persist created object(s).
    }
    public override void UpdateDatabaseBeforeUpdateSchema() {
        base.UpdateDatabaseBeforeUpdateSchema();
    }
}
