using DevExpress.ExpressApp.EFCore.Updating;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using DevExpress.Persistent.BaseImpl.EF.PermissionPolicy;
using DevExpress.Persistent.BaseImpl.EF;
using DevExpress.ExpressApp.Design;
using DevExpress.ExpressApp.EFCore.DesignTime;

namespace DataspaceOperator.Xaf.Module.BusinessObjects;


[TypesInfoInitializer(typeof(DbContextTypesInfoInitializer<XafEFCoreDbContext>))]
public class XafEFCoreDbContext : DbContext {
    public XafEFCoreDbContext(DbContextOptions<XafEFCoreDbContext> options) : base(options) {
    }
    //public DbSet<ModuleInfo> ModulesInfo { get; set; }

    // Dataspace operator business objects
    public DbSet<ParticipantEntity> Participants { get; set; }
    public DbSet<TrustedIssuerEntity> TrustedIssuers { get; set; }
    public DbSet<CredentialTypeEntity> CredentialTypes { get; set; }
    public DbSet<CredentialDefinitionEntity> CredentialDefinitions { get; set; }
    public DbSet<IssuedCredentialEntity> IssuedCredentials { get; set; }
    public DbSet<StatusListStateEntity> StatusListState { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        base.OnModelCreating(modelBuilder);
        modelBuilder.UseDeferredDeletion(this);
        modelBuilder.UseOptimisticLock();
        modelBuilder.SetOneToManyAssociationDeleteBehavior(DeleteBehavior.SetNull, DeleteBehavior.Cascade);
        modelBuilder.HasChangeTrackingStrategy(ChangeTrackingStrategy.ChangingAndChangedNotificationsWithOriginalValues);
        modelBuilder.UsePropertyAccessMode(PropertyAccessMode.PreferFieldDuringConstruction);
    }
}
