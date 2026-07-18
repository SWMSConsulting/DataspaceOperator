using DevExpress.ExpressApp;
using DevExpress.Data.Filtering;
using DevExpress.Persistent.Base;
using DevExpress.ExpressApp.Updating;
using DevExpress.ExpressApp.EF;
using DevExpress.ExpressApp.Security;
using DevExpress.ExpressApp.SystemModule;
using DevExpress.Persistent.BaseImpl.EF;
using DevExpress.Persistent.BaseImpl.EF.PermissionPolicy;
using Microsoft.Extensions.Configuration;
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

        // --- Security: roles are always created ---
        var defaultRole = CreateDefaultRole();
        var adminRole = CreateAdminRole();
        ObjectSpace.CommitChanges();

        var userManager = ObjectSpace.ServiceProvider.GetRequiredService<UserManager>();

        // Production-safe bootstrap: create the initial admin ONCE from configuration, only when a
        // password is provided (env Bootstrap__AdminPassword, from a Secret). No empty passwords.
        var config = ObjectSpace.ServiceProvider.GetService<IConfiguration>();
        var bootstrapUser = config?["Bootstrap:AdminUserName"];
        if(string.IsNullOrWhiteSpace(bootstrapUser)) bootstrapUser = "Admin";
        var bootstrapPassword = config?["Bootstrap:AdminPassword"];
        if(!string.IsNullOrEmpty(bootstrapPassword) &&
           userManager.FindUserByName<ApplicationUser>(ObjectSpace, bootstrapUser) == null) {
            _ = userManager.CreateUser<ApplicationUser>(ObjectSpace, bootstrapUser, bootstrapPassword,
                user => user.Roles.Add(adminRole));
        }

#if !RELEASE
        // Local dev convenience: Admin/User with empty passwords (compiled out in Release).
        if(userManager.FindUserByName<ApplicationUser>(ObjectSpace, "Admin") == null) {
            _ = userManager.CreateUser<ApplicationUser>(ObjectSpace, "Admin", "", user => user.Roles.Add(adminRole));
        }
        if(userManager.FindUserByName<ApplicationUser>(ObjectSpace, "User") == null) {
            _ = userManager.CreateUser<ApplicationUser>(ObjectSpace, "User", "", user => user.Roles.Add(defaultRole));
        }
#endif
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

    private PermissionPolicyRole CreateAdminRole() {
        var adminRole = ObjectSpace.FirstOrDefault<PermissionPolicyRole>(r => r.Name == "Administrators");
        if(adminRole == null) {
            adminRole = ObjectSpace.CreateObject<PermissionPolicyRole>();
            adminRole.Name = "Administrators";
            adminRole.IsAdministrative = true;
        }
        return adminRole;
    }

    private PermissionPolicyRole CreateDefaultRole() {
        var defaultRole = ObjectSpace.FirstOrDefault<PermissionPolicyRole>(role => role.Name == "Default");
        if(defaultRole == null) {
            defaultRole = ObjectSpace.CreateObject<PermissionPolicyRole>();
            defaultRole.Name = "Default";

            defaultRole.AddObjectPermissionFromLambda<ApplicationUser>(SecurityOperations.Read, cm => cm.ID == (Guid)CurrentUserIdOperator.CurrentUserId(), SecurityPermissionState.Allow);
            defaultRole.AddNavigationPermission(@"Application/NavigationItems/Items/Default/Items/MyDetails", SecurityPermissionState.Allow);
            defaultRole.AddMemberPermissionFromLambda<ApplicationUser>(SecurityOperations.Write, "ChangePasswordOnFirstLogon", cm => cm.ID == (Guid)CurrentUserIdOperator.CurrentUserId(), SecurityPermissionState.Allow);
            defaultRole.AddMemberPermissionFromLambda<ApplicationUser>(SecurityOperations.Write, "StoredPassword", cm => cm.ID == (Guid)CurrentUserIdOperator.CurrentUserId(), SecurityPermissionState.Allow);
            defaultRole.AddTypePermissionsRecursively<PermissionPolicyRole>(SecurityOperations.Read, SecurityPermissionState.Deny);
            defaultRole.AddObjectPermission<ModelDifference>(SecurityOperations.ReadWriteAccess, "UserId = ToStr(CurrentUserId())", SecurityPermissionState.Allow);
            defaultRole.AddObjectPermission<ModelDifferenceAspect>(SecurityOperations.ReadWriteAccess, "Owner.UserId = ToStr(CurrentUserId())", SecurityPermissionState.Allow);
            defaultRole.AddTypePermissionsRecursively<ModelDifference>(SecurityOperations.Create, SecurityPermissionState.Allow);
            defaultRole.AddTypePermissionsRecursively<ModelDifferenceAspect>(SecurityOperations.Create, SecurityPermissionState.Allow);
        }
        return defaultRole;
    }
}
