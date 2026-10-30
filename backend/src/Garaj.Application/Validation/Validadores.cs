using FluentValidation;
using Garaj.Application.Appointments;
using Garaj.Application.Auth;
using Garaj.Application.Customers;
using Garaj.Application.Inventory;
using Garaj.Application.Media;
using Garaj.Application.Quotes;
using Garaj.Application.Sales;
using Garaj.Application.ServiceRequests;
using Garaj.Application.Platform;
using Garaj.Application.WorkOrders;
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
        RuleFor(x => x)
            .Must(x => (x.VehicleId is null) != (x.EquipmentId is null))
            .WithMessage("La cita es para un vehículo o para un equipo.");

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

public class SaveEquipmentValidator : AbstractValidator<SaveEquipmentRequest>
{
    public SaveEquipmentValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.EquipmentTypeId).NotEmpty().WithMessage("Diga qué clase de equipo es.");
        RuleFor(x => x.Brand).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Model).NotEmpty().MaximumLength(80);
        RuleFor(x => x.SerialNumber).MaximumLength(60);
        RuleFor(x => x.Tag).MaximumLength(30);
        RuleFor(x => x.Color).MaximumLength(40);
        RuleFor(x => x.Accessories).MaximumLength(500);
        RuleFor(x => x.Notes).MaximumLength(1000);

        // Lo propio del tipo viaja como JSON y el servidor no lo interpreta, pero sí le pone
        // techo: es una ficha de equipo, no un almacén.
        RuleFor(x => x.Attributes).MaximumLength(4000);
    }
}

public class SaveEquipmentTypeValidator : AbstractValidator<SaveEquipmentTypeRequest>
{
    public SaveEquipmentTypeValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Icon).MaximumLength(40);
    }
}

public class CreateServiceRequestValidator : AbstractValidator<CreateServiceRequestRequest>
{
    public CreateServiceRequestValidator()
    {
        RuleFor(x => x.BranchId).NotEmpty();

        // Uno de los dos, nunca los dos: el taller trabaja con vehículos o con equipos.
        RuleFor(x => x)
            .Must(x => (x.VehicleId is null) != (x.EquipmentId is null))
            .WithMessage("El requerimiento es de un vehículo o de un equipo.");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Anote para qué entra el vehículo.")
            .MaximumLength(2000);

        RuleFor(x => x.ReportedSymptoms).MaximumLength(2000);

        RuleFor(x => x.Mileage).GreaterThanOrEqualTo(0)
            .When(x => x.Mileage.HasValue)
            .WithMessage("El kilometraje no puede ser negativo.");
    }
}

/// <summary>
/// El repuesto del catálogo. Lo usa la pantalla de Inventario y, renglón por renglón, la
/// carga desde Excel: así una celda de mil caracteres sale como el error de esa fila y no
/// como un 500 de Npgsql a mitad de un archivo de trescientas.
/// </summary>
public class SavePartValidator : AbstractValidator<SavePartRequest>
{
    public SavePartValidator()
    {
        // Con mensaje propio: estos errores los lee el taller renglón por renglón al cargar
        // su catálogo, y el de la librería nombra la propiedad en inglés.
        RuleFor(x => x.Sku).NotEmpty().WithMessage("Falta el SKU.")
            .MaximumLength(50).WithMessage("El SKU no puede pasar de 50 letras.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Falta el nombre.")
            .MaximumLength(200).WithMessage("El nombre no puede pasar de 200 letras.");
        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("La descripción no puede pasar de 1000 letras.");
        RuleFor(x => x.Brand)
            .MaximumLength(80).WithMessage("La marca no puede pasar de 80 letras.");
        RuleFor(x => x.Category)
            .MaximumLength(80).WithMessage("La categoría no puede pasar de 80 letras.");
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(10)
            .WithMessage("La unidad no puede pasar de 10 letras: u, lt, jgo, par…");

        RuleFor(x => x.CostPrice).GreaterThanOrEqualTo(0)
            .WithMessage("El costo no puede ser negativo.");
        RuleFor(x => x.SalePrice).GreaterThanOrEqualTo(0)
            .WithMessage("El precio de venta no puede ser negativo.");
    }
}

/// <summary>
/// El servicio de mano de obra. Mismo papel que el de repuestos, y además la regla que hace
/// que el precio exista: con precio fijo manda <c>FixedPrice</c>; si no, horas × tarifa.
/// </summary>
public class SaveLaborServiceValidator : AbstractValidator<SaveLaborServiceRequest>
{
    public SaveLaborServiceValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage("Falta el código.")
            .MaximumLength(30).WithMessage("El código no puede pasar de 30 letras.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Falta el nombre.")
            .MaximumLength(200).WithMessage("El nombre no puede pasar de 200 letras.");
        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("La descripción no puede pasar de 1000 letras.");
        RuleFor(x => x.Category)
            .MaximumLength(80).WithMessage("La categoría no puede pasar de 80 letras.");

        RuleFor(x => x.FixedPrice).GreaterThan(0)
            .When(x => x.IsFixedPrice)
            .WithMessage("Cobro fijo: falta el precio fijo.");

        RuleFor(x => x.HourlyRate).GreaterThan(0)
            .When(x => !x.IsFixedPrice)
            .WithMessage("Cobro por hora: falta la tarifa por hora.");

        RuleFor(x => x.StandardHours).GreaterThan(0)
            .When(x => !x.IsFixedPrice)
            .WithMessage("Cobro por hora: falta cuántas horas lleva.");

        // Negativos en el otro camino: no los usa nadie, pero guardados quedan de trampa.
        RuleFor(x => x.FixedPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.HourlyRate).GreaterThanOrEqualTo(0);
        RuleFor(x => x.StandardHours).GreaterThanOrEqualTo(0);
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

/// <summary>
/// Nuestra facturación. Los largos salen de las columnas, y los rangos del régimen: un CAI
/// mal tecleado se descubre cuando el taller lleva la factura al contador, que es tarde.
/// </summary>
public class SavePlatformSettingsValidator : AbstractValidator<SavePlatformSettingsRequest>
{
    public SavePlatformSettingsValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Hace falta el nombre con el que se emite.")
            .MaximumLength(120);

        RuleFor(x => x.LegalName).MaximumLength(200);
        RuleFor(x => x.TaxId).MaximumLength(20);
        RuleFor(x => x.Phone).MaximumLength(40);
        RuleFor(x => x.Email).MaximumLength(200);
        RuleFor(x => x.Address).MaximumLength(300);

        RuleFor(x => x.DefaultTaxRate)
            .InclusiveBetween(0, 100).WithMessage("El ISV tiene que ir entre 0 y 100.");

        RuleFor(x => x.InvoicePrefix)
            .NotEmpty().WithMessage("El comprobante necesita un prefijo, como «C».")
            .MaximumLength(8);
    }
}

public class SavePlatformFiscalRangeValidator : AbstractValidator<SavePlatformFiscalRangeRequest>
{
    public SavePlatformFiscalRangeValidator()
    {
        RuleFor(x => x.Cai)
            .NotEmpty().WithMessage("Escriba el CAI que autorizó el SAR.")
            .MaximumLength(50);

        RuleFor(x => x.EstablishmentCode)
            .NotEmpty().MaximumLength(3).WithMessage("El código del establecimiento lleva 3 dígitos.");

        RuleFor(x => x.PointOfSaleCode)
            .NotEmpty().MaximumLength(3).WithMessage("El código del punto de emisión lleva 3 dígitos.");

        RuleFor(x => x.DocumentType)
            .NotEmpty().MaximumLength(2).WithMessage("El tipo de documento lleva 2 dígitos.");

        RuleFor(x => x.RangeStart)
            .GreaterThan(0).WithMessage("El inicio del rango tiene que ser mayor que cero.");

        RuleFor(x => x.RangeEnd)
            .GreaterThanOrEqualTo(x => x.RangeStart)
            .WithMessage("El final del rango no puede ser menor que el inicio.");
    }
}

public class UpdateTenantValidator : AbstractValidator<UpdateTenantRequest>
{
    public UpdateTenantValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("El taller necesita un nombre.")
            .MaximumLength(200);

        RuleFor(x => x.LegalName).MaximumLength(200);
        RuleFor(x => x.TaxId).MaximumLength(20);
        RuleFor(x => x.Phone).MaximumLength(40);
        RuleFor(x => x.Email).MaximumLength(200);
        RuleFor(x => x.Address).MaximumLength(300);
    }
}

public class VoidPaymentValidator : AbstractValidator<VoidPaymentRequest>
{
    public VoidPaymentValidator() =>
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Diga por qué se anula el pago.")
            .MaximumLength(300);
}

public class VoidInvoiceValidator : AbstractValidator<VoidInvoiceRequest>
{
    public VoidInvoiceValidator() =>
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Diga por qué se anula: es lo que explica el número que falta.")
            .MaximumLength(300);
}

/// <summary>
/// El pago de la mensualidad y, con él, el documento que se le entrega al taller.
/// </summary>
public class RegisterSubscriptionPaymentValidator
    : AbstractValidator<Garaj.Application.Platform.RegisterPaymentRequest>
{
    public RegisterSubscriptionPaymentValidator()
    {
        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("El monto del pago debe ser mayor que cero.");

        RuleFor(x => x.Months)
            .InclusiveBetween(1, 24).WithMessage("El pago cubre entre 1 y 24 meses.");

        RuleFor(x => x.Method).MaximumLength(40);
        RuleFor(x => x.Reference).MaximumLength(80);
        RuleFor(x => x.Note).MaximumLength(300);

        RuleForEach(x => x.Lines).ChildRules(linea =>
        {
            linea.RuleFor(l => l.Description)
                .NotEmpty().WithMessage("Cada concepto necesita decir de qué es.")
                .MaximumLength(200);

            linea.RuleFor(l => l.Quantity)
                .GreaterThan(0).WithMessage("La cantidad del concepto debe ser mayor que cero.");

            linea.RuleFor(l => l.UnitPrice)
                .GreaterThanOrEqualTo(0).WithMessage("El precio del concepto no puede ser negativo.");
        });
    }
}

/// <summary>
/// El trabajo frecuente, que es la pantalla donde se teclea más de una fila a la vez: sin
/// esto, un título largo en el paso catorce revienta en Npgsql y el taller recibe un 500 mudo
/// después de haber escrito todo el paquete.
/// </summary>
public class SaveJobTemplateValidator : AbstractValidator<SaveJobTemplateRequest>
{
    public SaveJobTemplateValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("El trabajo necesita un nombre.")
            .MaximumLength(120);

        RuleFor(x => x.Description).MaximumLength(1000);

        RuleForEach(x => x.Tasks).ChildRules(paso =>
        {
            paso.RuleFor(t => t.Title)
                .NotEmpty().WithMessage("Cada paso necesita decir qué se hace.")
                .MaximumLength(200);

            paso.RuleFor(t => t.Description).MaximumLength(2000);

            paso.RuleFor(t => t.EstimatedHours)
                .GreaterThanOrEqualTo(0).When(t => t.EstimatedHours.HasValue)
                .WithMessage("Las horas no pueden ser negativas.");

            paso.RuleFor(t => t.ManualLaborPrice)
                .GreaterThanOrEqualTo(0).When(t => t.ManualLaborPrice.HasValue)
                .WithMessage("El precio del paso no puede ser negativo.");
        });

        RuleForEach(x => x.Parts).ChildRules(repuesto =>
        {
            repuesto.RuleFor(p => p.Description).MaximumLength(200);

            repuesto.RuleFor(p => p.Quantity)
                .GreaterThan(0).WithMessage("La cantidad de cada repuesto debe ser mayor que cero.");

            repuesto.RuleFor(p => p.UnitPrice)
                .GreaterThanOrEqualTo(0).When(p => p.UnitPrice.HasValue)
                .WithMessage("El precio del repuesto no puede ser negativo.");
        });
    }
}

/// <summary>
/// Las tres puertas que se abren sin sesión. Aquí el tope no es la columna —no se guarda
/// nada—: es no dejar que un desconocido mande un megabyte de correo o de contraseña para que
/// el servidor lo normalice y lo hashee.
/// </summary>
public class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(200);
    }
}

public class RefreshTokenValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(500);
}

public class DeleteAccountValidator : AbstractValidator<DeleteAccountRequest>
{
    public DeleteAccountValidator() => RuleFor(x => x.Confirm).NotEmpty().MaximumLength(50);
}
