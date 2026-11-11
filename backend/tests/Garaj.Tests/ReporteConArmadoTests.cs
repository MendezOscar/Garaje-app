using Garaj.Application.Abstractions;
using Garaj.Application.Assembly;
using Garaj.Application.Common;
using Garaj.Application.Sales;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Assembly;
using Garaj.Infrastructure.Persistence;
using Garaj.Infrastructure.Persistence.Interceptors;
using Garaj.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Tests;

/// <summary>
/// El armado dentro del reporte de ingresos: que no reviente y que sume.
/// </summary>
public class ReporteConArmadoTests
{
    private const string CadenaLocal =
        "Host=localhost;Port=5434;Database=garaj;Username=garaj;Password=garaj-dev-secret";

    private static GarajDbContext Contexto(Guid taller, Guid usuario)
    {
        var cadena = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? CadenaLocal;
        var contexto = new TenantContext();
        contexto.Initialize(taller, usuario, AppRoles.Owner, [], null);

        var options = new DbContextOptionsBuilder<GarajDbContext>()
            .UseNpgsql(cadena)
            .AddInterceptors(new AuditableEntityInterceptor(contexto, new DateTimeProvider()))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new GarajDbContext(options, contexto);
    }

    [RequiereBaseLocal]
    public async Task El_armado_facturado_entra_en_los_ingresos_del_rango()
    {
        var taller = Guid.NewGuid();
        var usuario = Guid.NewGuid();
        var sucursal = Guid.NewGuid();
        var corto = $"{taller:N}"[..8].ToUpperInvariant();

        await using var db = Contexto(taller, usuario);

        db.Tenants.Add(new Tenant
        {
            Id = taller, Name = $"Armado {corto}", Currency = "HNL",
            DefaultPhoneCountryCode = "504", WorksWith = WorkshopKind.Vehicles,
            AssemblyEnabled = true
        });

        db.Branches.Add(new Branch
        {
            Id = sucursal, TenantId = taller, Name = "Matriz", Code = corto[..3], IsActive = true
        });

        var contratista = new AssemblyContractor
        {
            TenantId = taller, Name = "Electra", BranchId = sucursal, IsActive = true
        };
        var modelo = new AssemblyModel { TenantId = taller, Name = "DM250", IsActive = true };
        var bodega = new AssemblyWarehouse { TenantId = taller, Name = "Bodega 1", IsActive = true };

        db.AssemblyContractors.Add(contratista);
        db.AssemblyModels.Add(modelo);
        db.AssemblyWarehouses.Add(bodega);

        var corte = new AssemblySettlement
        {
            TenantId = taller,
            Kind = AssemblySettlementKind.Billing,
            PeriodFrom = new DateOnly(2026, 11, 1),
            PeriodTo = new DateOnly(2026, 11, 30),
            ContractorId = contratista.Id,
            Count = 1,
            Total = 1000m
        };

        db.AssemblySettlements.Add(corte);

        db.MotorcycleAssemblies.Add(new MotorcycleAssembly
        {
            TenantId = taller, BranchId = sucursal, ContractorId = contratista.Id,
            ModelId = modelo.Id, WarehouseId = bodega.Id, TechnicianUserId = usuario,
            AssembledOn = DateOnly.FromDateTime(DateTime.UtcNow), Serial = $"ARM{corto}0000001",
            Charge = 1000m, TechnicianPay = 300m, Expense = 50m,
            BillingSettlementId = corte.Id
        });

        await db.SaveChangesAsync();

        var servicio = new ReportService(db, db.TenantContext, new DateTimeProvider(), tenants: null!);

        var hoy = DateTime.UtcNow;
        var reporte = await servicio.RevenueAsync(new RevenueQuery
        {
            From = new DateTimeOffset(hoy.Year, hoy.Month, 1, 0, 0, 0, TimeSpan.Zero),
            To = new DateTimeOffset(hoy.Year, hoy.Month, hoy.Day, 0, 0, 0, TimeSpan.Zero),
            GroupBy = RevenueGrouping.Day
        });

        Assert.Equal(1000m, reporte.AssemblyRevenue);
        Assert.Equal(1000m, reporte.Total);
        Assert.Contains(reporte.Points, p => p.AssemblyRevenue == 1000m);
    }
}
