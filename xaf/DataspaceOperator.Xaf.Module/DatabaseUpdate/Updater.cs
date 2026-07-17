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
        var own = ObjectSpace.FirstOrDefault<TrustedIssuerEntity>(x => x.Did == ownIssuerDid);
        if(own == null) {
            own = ObjectSpace.CreateObject<TrustedIssuerEntity>();
            own.Did = ownIssuerDid;
            own.IsOwnIssuer = true;
            own.SupportedTypesCsv = "";
            ObjectSpace.CommitChanges();
        }

        // Seed example participants from the MXD sample (Alice & Bob).
        SeedParticipant("Alice GmbH", "BPNL000000000001", "did:web:alice-ih%3A7083:alice");
        SeedParticipant("Bob AG", "BPNL000000000002", "did:web:bob-ih%3A7083:bob");
        ObjectSpace.CommitChanges();

        void SeedParticipant(string name, string bpn, string did) {
            var p = ObjectSpace.FirstOrDefault<ParticipantEntity>(x => x.Did == did);
            if(p == null) {
                p = ObjectSpace.CreateObject<ParticipantEntity>();
                p.Name = name;
                p.Bpn = bpn;
                p.Did = did;
                p.State = ParticipantState.Active;
                p.OnboardedUtc = DateTime.UtcNow;
            }
            var entry = ObjectSpace.FirstOrDefault<BpnDidEntryEntity>(x => x.Bpn == bpn);
            if(entry == null) {
                entry = ObjectSpace.CreateObject<BpnDidEntryEntity>();
                entry.Bpn = bpn;
                entry.Did = did;
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
