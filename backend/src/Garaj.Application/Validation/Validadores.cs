using FluentValidation;
using Garaj.Application.Appointments;
using Garaj.Application.Customers;
using Garaj.Application.Media;
using Garaj.Application.Quotes;
using Garaj.Application.Sales;
using Garaj.Application.ServiceRequests;
using Garaj.Domain.Rules;

namespace Garaj.Application.Validation;

/// <summary>
/// La forma de lo que entra: obligatorios, largos, rangos y enums. Las reglas de negocio
/// —si la sucursal existe, si el día cae dentro de la agenda, si ya tiene citas abiertas—
/// siguen en los servicios, que son los que tienen base de datos y reloj.
///
/// Los largos salen de las configuraciones de EF, que es donde se decidió el ancho de cada
/// columna. Sin esto, pasarse hace reventar a Npgsql y el cliente recibe un 500 mudo en vez
/// de un 400 que le diga qué campo sobra. Si cambia un <c>HasMaxLength</c>, cambie el de aquí.
/// </summary>
public class BookAppointmentValidator : AbstractValidator<BookAppointmentRequest>
{
    public BookAppointmentValidator()
    {
        RuleFor(x => x.ContactName)
            .NotEmpty().WithMessage("Hace falta su nombre.")
            .MaximumLength(200);

        RuleFor(x => x.ContactPhone)
            .Must(t => AppointmentRules.Telefono(t) is not null)
            .WithMessage("Hace falta un teléfono de ocho dígitos para poder contestarle.");

        RuleFor(x => x.VehicleNote).MaximumLength(300);

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Diga qué necesita para poder apartarle el día.")
            .MaximumLength(2000);

        RuleFor(x => x.Slot).IsInEnum().WithMessage("Escoja la mañana o la tarde.");
    }
}

public class RequestAppointmentValidator : AbstractValidator<RequestAppointmentRequest>
{
    public RequestAppointmentValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Diga qué necesita.")
            .MaximumLength(2000);

        RuleFor(x => x.Slot).IsInEnum().WithMessage("Escoja la mañana o la tarde.");
    }
}

public class RespondToQuoteValidator : AbstractValidator<RespondToQuoteRequest>
{
    public RespondToQuoteValidator() => RuleFor(x => x.Note).MaximumLength(1000);
}

public class CreateUploadValidator : AbstractValidator<CreateUploadRequest>
{
    public CreateUploadValidator()
    {
        RuleFor(x => x.OwnerType).IsInEnum();
        RuleFor(x => x.OwnerId).NotEmpty();
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SizeBytes).GreaterThan(0);
        RuleFor(x => x.FileName).MaximumLength(300);
        RuleFor(x => x.Caption).MaximumLength(500);
    }
}

public class SaveCustomerValidator : AbstractValidator<SaveCustomerRequest>
{
    public SaveCustomerValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Hace falta el nombre del cliente.")
            .MaximumLength(200);

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Hace falta el teléfono: es por donde se le manda todo.")
            .MaximumLength(25);

        RuleFor(x => x.Email).MaximumLength(200).EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.DocumentId).MaximumLength(30);
        RuleFor(x => x.TaxId).MaximumLength(50);
        RuleFor(x => x.BillingName).MaximumLength(200);
        RuleFor(x => x.Address).MaximumLength(300);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class SaveVehicleValidator : AbstractValidator<SaveVehicleRequest>
{
    public SaveVehicleValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Brand).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Model).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Plate).MaximumLength(15);
        RuleFor(x => x.Vin).MaximumLength(25);
        RuleFor(x => x.Color).MaximumLength(40);
        RuleFor(x => x.Notes).MaximumLength(1000);

        // Un año de cuatro cifras con holgura por arriba: los modelos salen antes que el año.
        RuleFor(x => x.Year).InclusiveBetween(1900, 2100)
            .When(x => x.Year.HasValue)
            .WithMessage("El año no parece un año.");

        RuleFor(x => x.Mileage).GreaterThanOrEqualTo(0)
            .When(x => x.Mileage.HasValue)
            .WithMessage("El kilometraje no puede ser negativo.");
    }
}

public class CreateServiceRequestValidator : AbstractValidator<CreateServiceRequestRequest>
{
    public CreateServiceRequestValidator()
    {
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.VehicleId).NotEmpty();

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Anote para qué entra el vehículo.")
            .MaximumLength(2000);

        RuleFor(x => x.ReportedSymptoms).MaximumLength(2000);

        RuleFor(x => x.Mileage).GreaterThanOrEqualTo(0)
            .When(x => x.Mileage.HasValue)
            .WithMessage("El kilometraje no puede ser negativo.");
    }
}

public class SaveExpenseValidator : AbstractValidator<SaveExpenseRequest>
{
    public SaveExpenseValidator()
    {
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.PaymentMethod).IsInEnum();

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Diga en qué se gastó.")
            .MaximumLength(300);

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("El monto tiene que ser mayor que cero.");

        RuleFor(x => x.SupplierName).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}
