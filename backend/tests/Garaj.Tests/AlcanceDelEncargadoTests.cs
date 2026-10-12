using Garaj.Application.Abstractions;
using Garaj.Application.Common;

namespace Garaj.Tests;

/// <summary>
/// Qué alcanza cada perfil.
///
/// El Encargado es el perfil nuevo y el delicado: manda como el Dueño pero solo dentro de sus
/// sucursales, así que lo que se prueba aquí es justo el borde —que mande, que no mande en lo
/// del taller entero, y que una sucursal ajena le conteste 404 y no 403, que diría que existe—.
/// </summary>
public class AlcanceDelEncargadoTests
{
    private static readonly Guid Centro = Guid.NewGuid();
    private static readonly Guid Sur = Guid.NewGuid();

    private static AccessScope Encargado(params Guid[] sucursales) =>
        Alcance(AppRoles.Manager, sucursales);

    private static AccessScope Alcance(string perfil, params Guid[] sucursales)
    {
        var contexto = new ContextoFalso(perfil, sucursales);
        return AccessScope.From(contexto);
    }

    [Fact]
    public void El_encargado_manda()
    {
        var scope = Encargado(Centro);

        Assert.True(scope.IsManager);
        Assert.True(scope.IsManagement);
        Assert.False(scope.IsOwner);
        scope.EnsureManagement();
    }

    [Fact]
    public void El_dueño_tambien_manda()
    {
        var scope = Alcance(AppRoles.Owner);

        Assert.True(scope.IsManagement);
        scope.EnsureManagement();
        scope.EnsureOwner();
    }

    [Fact]
    public void El_encargado_no_es_dueño()
    {
        // Los ajustes del taller, los catálogos, los gastos y el alta de usuarios.
        Assert.Throws<ForbiddenException>(() => Encargado(Centro).EnsureOwner());
    }

    [Fact]
    public void El_tecnico_no_manda()
    {
        Assert.Throws<ForbiddenException>(
            () => Alcance(AppRoles.Technician, Centro).EnsureManagement());
    }

    [Fact]
    public void El_encargado_opera_en_su_sucursal()
    {
        Encargado(Centro, Sur).EnsureBranchAllowed(Sur);
    }

    [Fact]
    public void La_sucursal_ajena_no_existe_para_el_encargado()
    {
        // 404 y no 403: un 403 confirmaría que esa sucursal existe.
        Assert.Throws<NotFoundException>(() => Encargado(Centro).EnsureBranchAllowed(Sur));
    }

    [Fact]
    public void El_filtro_del_encargado_son_sus_sucursales()
    {
        var filtro = Encargado(Centro, Sur).BranchFilter();

        Assert.NotNull(filtro);
        Assert.Equal([Centro, Sur], filtro);
    }

    [Fact]
    public void El_dueño_no_lleva_filtro_de_sucursal()
    {
        // Null es «todas»: es lo que distingue al que ve el taller entero del que ve lo suyo.
        Assert.Null(Alcance(AppRoles.Owner).BranchFilter());
    }

    [Fact]
    public void El_perfil_de_plataforma_no_se_crea_desde_el_taller()
    {
        Assert.Contains(AppRoles.Manager, AppRoles.DelTaller);
        Assert.DoesNotContain(AppRoles.Platform, AppRoles.DelTaller);
    }

    private sealed class ContextoFalso(string rol, IReadOnlyCollection<Guid> sucursales)
        : ITenantContext
    {
        public Guid? TenantId => Guid.Empty;
        public Guid? UserId => Guid.Empty;
        public string? Role => rol;
        public IReadOnlyCollection<Guid> BranchIds => sucursales;
        public Guid? CustomerId => null;
        public bool IsAuthenticated => true;
        public bool BypassTenantFilter { get; set; }

        public void Initialize(
            Guid? tenantId, Guid? userId, string? role,
            IReadOnlyCollection<Guid> branchIds, Guid? customerId) =>
            throw new NotSupportedException();

        public void SetTenant(Guid tenantId) => throw new NotSupportedException();
    }
}
