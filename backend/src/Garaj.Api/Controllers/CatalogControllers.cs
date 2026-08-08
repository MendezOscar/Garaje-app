using Garaj.Application.Branches;
using Garaj.Application.Common;
using Garaj.Application.Customers;
using Garaj.Application.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garaj.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/branches")]
public class BranchesController(IBranchService service) : ControllerBase
{
    /// <summary>Sucursales visibles para el usuario. El Técnico solo ve las suyas.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BranchDto>>> List(
        [FromQuery] bool includeInactive, CancellationToken ct)
        => Ok(await service.ListAsync(includeInactive, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BranchDto>> Get(Guid id, CancellationToken ct)
        => Ok(await service.GetAsync(id, ct));

    [HttpPost]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<BranchDto>> Create(SaveBranchRequest request, CancellationToken ct)
    {
        var branch = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = branch.Id }, branch);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<BranchDto>> Update(Guid id, SaveBranchRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));
}

[ApiController]
[Authorize(Policy = AppPolicies.OwnerOnly)]
[Route("api/users")]
public class UsersController(IUserService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> List(
        [FromQuery] string? role, CancellationToken ct)
        => Ok(await service.ListAsync(role, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserDto>> Get(Guid id, CancellationToken ct)
        => Ok(await service.GetAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var user = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserDto>> Update(Guid id, UpdateUserRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));

    /// <summary>Restablece la contraseña y cierra las sesiones abiertas de ese usuario.</summary>
    [HttpPost("{id:guid}/password")]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        await service.ResetPasswordAsync(id, request, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[Route("api/customers")]
public class CustomersController(ICustomerService service) : ControllerBase
{
    /// <summary>Busca por nombre, teléfono o placa de alguno de sus vehículos.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<CustomerDto>>> List(
        [FromQuery] CustomerQuery query, CancellationToken ct)
        => Ok(await service.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerDto>> Get(Guid id, CancellationToken ct)
        => Ok(await service.GetAsync(id, ct));

    [HttpPost]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<CustomerDto>> Create(SaveCustomerRequest request, CancellationToken ct)
    {
        var customer = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = customer.Id }, customer);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<CustomerDto>> Update(Guid id, SaveCustomerRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));
}

[ApiController]
[Authorize]
[Route("api/vehicles")]
public class VehiclesController(IVehicleService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<VehicleDto>>> List(
        [FromQuery] VehicleQuery query, CancellationToken ct)
        => Ok(await service.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<VehicleDto>> Get(Guid id, CancellationToken ct)
        => Ok(await service.GetAsync(id, ct));

    /// <summary>El Cliente puede registrar los suyos; el Dueño, los de cualquiera.</summary>
    [HttpPost]
    public async Task<ActionResult<VehicleDto>> Create(SaveVehicleRequest request, CancellationToken ct)
    {
        var vehicle = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = vehicle.Id }, vehicle);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<VehicleDto>> Update(Guid id, SaveVehicleRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));
}
