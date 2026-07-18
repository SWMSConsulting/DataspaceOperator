using DevExpress.ExpressApp.ApplicationBuilder;
using DevExpress.ExpressApp.Blazor.ApplicationBuilder;
using DevExpress.ExpressApp.Blazor.Services;
using DevExpress.Persistent.Base;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.EntityFrameworkCore;
using DataspaceOperator.Xaf.Blazor.Server.Services;
using DataspaceOperator.Endpoints;
using DataspaceOperator.Core.Protocol;
using DevExpress.ExpressApp.Security;
using DevExpress.Persistent.BaseImpl.EF.PermissionPolicy;

namespace DataspaceOperator.Xaf.Blazor.Server;

public class Startup {
    public Startup(IConfiguration configuration) {
        Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    // This method gets called by the runtime. Use this method to add services to the container.
    // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton(typeof(Microsoft.AspNetCore.SignalR.HubConnectionHandler<>), typeof(ProxyHubConnectionHandler<>));

        services.AddRazorPages();
        services.AddServerSideBlazor();
        services.AddHttpContextAccessor();
        services.AddScoped<CircuitHandler, CircuitHandlerProxy>();
        services.AddXaf(Configuration, builder => {
            builder.UseApplication<XafBlazorApplication>();
            builder.Modules
                .AddConditionalAppearance()
                .AddValidation(options => {
                    options.AllowValidationDetailsAccess = false;
                })
                .Add<DataspaceOperator.Xaf.Module.XafModule>()
                .Add<XafBlazorModule>();
            builder.ObjectSpaceProviders
                .AddSecuredEFCore(options => {
                    options.PreFetchReferenceProperties();
                })
                .WithDbContext<DataspaceOperator.Xaf.Module.BusinessObjects.XafEFCoreDbContext>((serviceProvider, options) => {
                    // Uncomment this code to use an in-memory database. This database is recreated each time the server starts. With the in-memory database, you don't need to make a migration when the data model is changed.
                    // Do not use this code in production environment to avoid data loss.
                    // We recommend that you refer to the following help topic before you use an in-memory database: https://docs.microsoft.com/en-us/ef/core/testing/in-memory
                    //options.UseInMemoryDatabase();
                    string connectionString = null;
                    if(Configuration.GetConnectionString("ConnectionString") != null) {
                        connectionString = Configuration.GetConnectionString("ConnectionString");
                    }
#if EASYTEST
                    if(Configuration.GetConnectionString("EasyTestConnectionString") != null) {
                        connectionString = Configuration.GetConnectionString("EasyTestConnectionString");
                    }
#endif
                    ArgumentNullException.ThrowIfNull(connectionString);
                    options.UseConnectionString(connectionString);
                })
                .AddNonPersistent();
            builder.Security
                .UseIntegratedMode(options => {
                    options.Lockout.Enabled = true;
                    options.RoleType = typeof(PermissionPolicyRole);
                    options.UserType = typeof(DataspaceOperator.Xaf.Module.BusinessObjects.ApplicationUser);
                    options.UserLoginInfoType = typeof(DataspaceOperator.Xaf.Module.BusinessObjects.ApplicationUserLoginInfo);
                    options.Events.OnSecurityStrategyCreated += securityStrategy => {
                        ((SecurityStrategy)securityStrategy).PermissionsReloadMode = PermissionsReloadMode.NoCache;
                    };
                })
                .AddPasswordAuthentication(options => {
                    options.IsSupportChangePassword = true;
                });
        });

        // Dataspace protocol services (framework-independent core) backed by the XAF object space.
        services.AddDataspaceProtocol(Configuration);

        var authentication = services.AddAuthentication(options => {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        });
        authentication.AddCookie(options => {
            options.LoginPath = "/LoginPage";
        });
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env) {
        if(env.IsDevelopment()) {
            app.UseDeveloperExceptionPage();
        }
        else {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. To change this for production scenarios, see: https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }
        app.UseHttpsRedirection();
        app.UseRequestLocalization();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.UseXaf();
        app.UseEndpoints(endpoints => {
            // Dataspace protocol endpoints (did:web, BDRS directory, DCP issuance, status list)
            endpoints.MapDataspaceProtocol();
            // Dev-only convenience trigger (real issuance in the UI: "Issue Membership Credential" action)
            if(env.IsDevelopment()) {
                endpoints.MapPost("/admin/issue", async (string did, string? type, DcpIssuanceService issuance, CancellationToken ct) => {
                    var r = await issuance.IssueAsync(did, type ?? "MembershipCredential", ct);
                    return Results.Ok(new { r.Id, r.CredentialType, delivery = r.Delivery.ToString(), credential = r.Jwt });
                });
                endpoints.MapPost("/admin/credentials/{id}/revoke", async (Guid id, DcpIssuanceService issuance, CancellationToken ct) => {
                    await issuance.RevokeAsync(id, ct);
                    return Results.Ok(new { revoked = id });
                });
            }
            endpoints.MapXafEndpoints();
            endpoints.MapBlazorHub();
            endpoints.MapFallbackToPage("/_Host");
            endpoints.MapControllers();
        });
    }
}
