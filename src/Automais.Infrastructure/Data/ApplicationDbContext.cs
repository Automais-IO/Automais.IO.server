using Automais.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Automais.Infrastructure.Data;

/// <summary>
/// Contexto do Entity Framework Core
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Gateway> Gateways => Set<Gateway>();
    public DbSet<TenantUser> TenantUsers => Set<TenantUser>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<VpnNetwork> VpnNetworks => Set<VpnNetwork>();
    public DbSet<VpnNetworkMembership> VpnNetworkMemberships => Set<VpnNetworkMembership>();
    public DbSet<Router> Routers => Set<Router>();
    public DbSet<RouterWireGuardPeer> RouterWireGuardPeers => Set<RouterWireGuardPeer>();
    public DbSet<RouterAllowedNetwork> RouterAllowedNetworks => Set<RouterAllowedNetwork>();
    public DbSet<RouterStaticRoute> RouterStaticRoutes => Set<RouterStaticRoute>();
    public DbSet<UserAllowedRoute> UserAllowedRoutes => Set<UserAllowedRoute>();
    public DbSet<RouterConfigLog> RouterConfigLogs => Set<RouterConfigLog>();
    public DbSet<RouterBackup> RouterBackups => Set<RouterBackup>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Configurar schema padrão
        if (Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            modelBuilder.HasDefaultSchema("public");
        }

        // Configuração de Tenant
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.ToTable("tenants");
            
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(100);
            
            entity.Property(e => e.Slug)
                .IsRequired()
                .HasMaxLength(50);
            
            entity.HasIndex(e => e.Slug)
                .IsUnique();
            
            entity.Property(e => e.Status)
                .IsRequired()
                .HasConversion<string>(); // Salva enum como string
            
            entity.Property(e => e.ChirpStackTenantId)
                .HasMaxLength(100);
            
            entity.Property(e => e.CreatedAt)
                .IsRequired();
            
            entity.Property(e => e.UpdatedAt)
                .IsRequired();
        });

        // Configuração de Gateway
        modelBuilder.Entity<Gateway>(entity =>
        {
            entity.ToTable("gateways");
            
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.TenantId)
                .IsRequired();
            
            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(100);
            
            entity.Property(e => e.GatewayEui)
                .IsRequired()
                .HasMaxLength(16);
            
            entity.HasIndex(e => e.GatewayEui)
                .IsUnique();
            
            entity.Property(e => e.Description)
                .HasMaxLength(500);
            
            entity.Property(e => e.Status)
                .IsRequired()
                .HasConversion<string>();
            
            entity.Property(e => e.CreatedAt)
                .IsRequired();
            
            entity.Property(e => e.UpdatedAt)
                .IsRequired();
            
            // Relacionamento com Tenant
            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.Gateways)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
            
            // Índice para busca por tenant
            entity.HasIndex(e => e.TenantId);
        });

        // Configuração de TenantUser
        modelBuilder.Entity<TenantUser>(entity =>
        {
            entity.ToTable("tenant_users");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.TenantId)
                .IsRequired();

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(120);

            entity.Property(e => e.Email)
                .IsRequired()
                .HasMaxLength(150);

            entity.HasIndex(e => new { e.TenantId, e.Email })
                .IsUnique();

            entity.Property(e => e.Role)
                .IsRequired()
                .HasConversion<string>();

            entity.Property(e => e.Status)
                .IsRequired()
                .HasConversion<string>();

            entity.Property(e => e.VpnDeviceName)
                .HasMaxLength(150);

            entity.Property(e => e.VpnPublicKey)
                .HasMaxLength(255);

            entity.Property(e => e.VpnIpAddress)
                .HasMaxLength(50);

            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            entity.Property(e => e.EmailDeliveryFailureMessage)
                .HasMaxLength(2000);

            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.Users)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configuração de Application
        modelBuilder.Entity<Application>(entity =>
        {
            entity.ToTable("applications");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.TenantId).IsRequired();

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(120);

            entity.HasIndex(e => new { e.TenantId, e.Name })
                .IsUnique();

            entity.Property(e => e.Status)
                .IsRequired()
                .HasConversion<string>();

            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.Applications)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configuração de Device
        modelBuilder.Entity<Device>(entity =>
        {
            entity.ToTable("devices");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.TenantId).IsRequired();
            entity.Property(e => e.ApplicationId).IsRequired();
            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(120);

            entity.Property(e => e.DevEui)
                .IsRequired()
                .HasMaxLength(16);

            entity.HasIndex(e => new { e.TenantId, e.DevEui })
                .IsUnique();

            entity.Property(e => e.Status)
                .IsRequired()
                .HasConversion<string>();

            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.Devices)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Application)
                .WithMany(a => a.Devices)
                .HasForeignKey(e => e.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.VpnNetwork)
                .WithMany(n => n.Devices)
                .HasForeignKey(e => e.VpnNetworkId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Configuração de VpnNetwork
        modelBuilder.Entity<VpnNetwork>(entity =>
        {
            entity.ToTable("vpn_networks");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.TenantId).IsRequired();
            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Slug)
                .IsRequired()
                .HasMaxLength(60);

            entity.Property(e => e.Cidr)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.ServerPrivateKey)
                .HasMaxLength(200);

            entity.Property(e => e.ServerPublicKey)
                .HasMaxLength(200);

            entity.Property(e => e.ServerEndpoint)
                .HasMaxLength(255);

            entity.HasIndex(e => new { e.TenantId, e.Slug })
                .IsUnique();

            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.VpnNetworks)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configuração de VpnNetworkMembership
        modelBuilder.Entity<VpnNetworkMembership>(entity =>
        {
            entity.ToTable("vpn_network_memberships");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.TenantId).IsRequired();
            entity.Property(e => e.VpnNetworkId).IsRequired();
            entity.Property(e => e.TenantUserId).IsRequired();

            entity.HasIndex(e => new { e.VpnNetworkId, e.TenantUserId })
                .IsUnique();

            entity.Property(e => e.AssignedIp)
                .HasMaxLength(50);

            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasOne(e => e.VpnNetwork)
                .WithMany(n => n.Memberships)
                .HasForeignKey(e => e.VpnNetworkId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.TenantUser)
                .WithMany(u => u.VpnMemberships)
                .HasForeignKey(e => e.TenantUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configuração de Router
        modelBuilder.Entity<Router>(entity =>
        {
            entity.ToTable("routers");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.TenantId).IsRequired();

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.SerialNumber)
                .HasMaxLength(100);

            entity.HasIndex(e => e.SerialNumber)
                .IsUnique()
                .HasFilter("\"SerialNumber\" IS NOT NULL");

            entity.Property(e => e.Model)
                .HasMaxLength(50);

            entity.Property(e => e.FirmwareVersion)
                .HasMaxLength(50);

            entity.Property(e => e.RouterOsApiUrl)
                .HasMaxLength(255);

            entity.Property(e => e.RouterOsApiUsername)
                .HasMaxLength(100);

            entity.Property(e => e.RouterOsApiPassword)
                .HasMaxLength(500);

            entity.Property(e => e.Status)
                .IsRequired()
                .HasConversion<string>();

            entity.Property(e => e.Latency);

            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            // Relacionamento com Tenant
            entity.HasOne(e => e.Tenant)
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.VpnNetwork)
                .WithMany()
                .HasForeignKey(e => e.VpnNetworkId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.TenantId);
        });

        // Configuração de RouterWireGuardPeer
        modelBuilder.Entity<RouterWireGuardPeer>(entity =>
        {
            entity.ToTable("router_wireguard_peers");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.RouterId).IsRequired();
            entity.Property(e => e.VpnNetworkId).IsRequired();

            entity.Property(e => e.PublicKey)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.PrivateKey)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.AllowedIps)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.Endpoint)
                .HasMaxLength(255);

            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            entity.HasOne(e => e.Router)
                .WithMany(r => r.WireGuardPeers)
                .HasForeignKey(e => e.RouterId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.VpnNetwork)
                .WithMany()
                .HasForeignKey(e => e.VpnNetworkId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.RouterId, e.VpnNetworkId })
                .IsUnique();

            entity.HasIndex(e => e.RouterId);
            entity.HasIndex(e => e.VpnNetworkId);
        });

        // Configuração de RouterAllowedNetwork
        modelBuilder.Entity<RouterAllowedNetwork>(entity =>
        {
            entity.ToTable("router_allowed_networks");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.RouterId).IsRequired();
            entity.Property(e => e.NetworkCidr)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.Description)
                .HasMaxLength(255);

            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasOne(e => e.Router)
                .WithMany()
                .HasForeignKey(e => e.RouterId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.RouterId, e.NetworkCidr })
                .IsUnique();

            entity.HasIndex(e => e.RouterId);
        });

        // Configuração de RouterStaticRoute
        modelBuilder.Entity<RouterStaticRoute>(entity =>
        {
            entity.ToTable("router_static_routes");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.RouterId).IsRequired();
            entity.Property(e => e.Destination)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.Gateway)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.Interface)
                .HasMaxLength(100);

            entity.Property(e => e.RoutingTable)
                .HasMaxLength(50);

            entity.Property(e => e.Description)
                .HasMaxLength(255);

            entity.Property(e => e.Comment)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.Status)
                .IsRequired()
                .HasConversion<string>();

            entity.Property(e => e.ErrorMessage)
                .HasMaxLength(500);

            entity.Property(e => e.RouterOsId)
                .HasMaxLength(100);

            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            entity.HasOne(e => e.Router)
                .WithMany(r => r.StaticRoutes)
                .HasForeignKey(e => e.RouterId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.RouterId, e.Destination })
                .IsUnique();

            entity.HasIndex(e => e.RouterId);
        });

        // Configuração de UserAllowedRoute
        modelBuilder.Entity<UserAllowedRoute>(entity =>
        {
            entity.ToTable("user_allowed_routes");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.RouterId).IsRequired();
            entity.Property(e => e.RouterAllowedNetworkId).IsRequired();
            entity.Property(e => e.NetworkCidr)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.Description)
                .HasMaxLength(255);

            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Router)
                .WithMany()
                .HasForeignKey(e => e.RouterId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.RouterAllowedNetwork)
                .WithMany()
                .HasForeignKey(e => e.RouterAllowedNetworkId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.UserId, e.RouterAllowedNetworkId })
                .IsUnique();

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.RouterId);
        });

        // Configuração de RouterConfigLog
        modelBuilder.Entity<RouterConfigLog>(entity =>
        {
            entity.ToTable("router_config_logs");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.RouterId).IsRequired();
            entity.Property(e => e.TenantId).IsRequired();

            entity.Property(e => e.Action)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.Category)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.ConfigPath)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.Source)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.RouterOsUser)
                .HasMaxLength(100);

            entity.Property(e => e.Timestamp).IsRequired();

            entity.HasOne(e => e.Router)
                .WithMany(r => r.ConfigLogs)
                .HasForeignKey(e => e.RouterId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Tenant)
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.PortalUser)
                .WithMany()
                .HasForeignKey(e => e.PortalUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.RouterId);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.PortalUserId);
            entity.HasIndex(e => e.Timestamp);
        });

        // Configuração de RouterBackup
        modelBuilder.Entity<RouterBackup>(entity =>
        {
            entity.ToTable("router_backups");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.RouterId).IsRequired();
            entity.Property(e => e.TenantId).IsRequired();

            entity.Property(e => e.FileName)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.FilePath)
                .IsRequired()
                .HasMaxLength(1000);

            entity.Property(e => e.RouterModel)
                .HasMaxLength(50);

            entity.Property(e => e.RouterOsVersion)
                .HasMaxLength(50);

            entity.Property(e => e.FileHash)
                .HasMaxLength(64);

            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasOne(e => e.Router)
                .WithMany(r => r.Backups)
                .HasForeignKey(e => e.RouterId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Tenant)
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.RouterId);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.CreatedAt);
        });
    }
}

