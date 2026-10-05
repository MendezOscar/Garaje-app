import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/tenant_repository.dart';
import '../../core/theme/garaj_brand.dart';
import '../../core/widgets/garaj_skeleton.dart';
import '../../core/widgets/garaj_states.dart';

/// Los ajustes del taller, desde el teléfono.
///
/// Tres grupos, los mismos que en el panel y en el mismo orden: lo que se imprime, lo que se
/// cobra, y lo que el técnico alcanza a ver. El logo y los rangos del SAR no están aquí a
/// propósito: son trabajo de escritorio y se hacen una vez.
class WorkshopSettingsScreen extends ConsumerStatefulWidget {
  const WorkshopSettingsScreen({super.key});

  @override
  ConsumerState<WorkshopSettingsScreen> createState() => _WorkshopSettingsScreenState();
}

class _WorkshopSettingsScreenState extends ConsumerState<WorkshopSettingsScreen> {
  final _nombre = TextEditingController();
  final _razonSocial = TextEditingController();
  final _rtn = TextEditingController();
  final _telefono = TextEditingController();
  final _codigoPais = TextEditingController();
  final _correo = TextEditingController();
  final _direccion = TextEditingController();
  final _isv = TextEditingController();
  final _garantia = TextEditingController();
  final _diasGracia = TextEditingController();
  final _porDia = TextEditingController();

  bool _cobraBodegaje = false;
  bool _tecnicoVePrecios = true;
  bool _cargada = false;
  bool _busy = false;

  @override
  void dispose() {
    for (final c in [
      _nombre, _razonSocial, _rtn, _telefono, _codigoPais, _correo, _direccion,
      _isv, _garantia, _diasGracia, _porDia,
    ]) {
      c.dispose();
    }
    super.dispose();
  }

  /// Una sola vez: después manda lo que se esté escribiendo. Con `initialValue` no alcanzaría,
  /// porque los ajustes llegan después del primer dibujado.
  void _llenar(TenantSettings s) {
    _nombre.text = s.name;
    _razonSocial.text = s.legalName ?? '';
    _rtn.text = s.taxId ?? '';
    _telefono.text = s.phone ?? '';
    _codigoPais.text = s.defaultPhoneCountryCode ?? '';
    _correo.text = s.email ?? '';
    _direccion.text = s.address ?? '';
    _isv.text = _sinCerosDeMas(s.defaultTaxRate);
    _garantia.text = s.defaultWarrantyDays.toString();
    _diasGracia.text = s.storageFreeDays.toString();
    _porDia.text = _sinCerosDeMas(s.storageDailyRate);
    _cobraBodegaje = s.chargesStorage;
    _tecnicoVePrecios = s.techniciansSeePrices;
  }

  static String _sinCerosDeMas(double v) =>
      v == v.roundToDouble() ? v.toInt().toString() : v.toString();

  Future<void> _guardar(TenantSettings actual) async {
    if (_nombre.text.trim().isEmpty) {
      _avisar('El taller necesita un nombre.');
      return;
    }

    setState(() => _busy = true);
    try {
      await ref.read(tenantRepositoryProvider).saveSettings(
            actual.copyWith(
              name: _nombre.text.trim(),
              legalName: _vacioEsNulo(_razonSocial.text),
              taxId: _vacioEsNulo(_rtn.text),
              phone: _vacioEsNulo(_telefono.text),
              email: _vacioEsNulo(_correo.text),
              address: _vacioEsNulo(_direccion.text),
              defaultTaxRate: double.tryParse(_isv.text.trim()) ?? 0,
              defaultPhoneCountryCode: _vacioEsNulo(_codigoPais.text),
              defaultWarrantyDays: int.tryParse(_garantia.text.trim()) ?? 0,
              chargesStorage: _cobraBodegaje,
              storageFreeDays: int.tryParse(_diasGracia.text.trim()) ?? 0,
              storageDailyRate: double.tryParse(_porDia.text.trim()) ?? 0,
              techniciansSeePrices: _tecnicoVePrecios,
            ),
          );

      // El ISV y la garantía los leen otras pantallas al cotizar y al facturar: si se quedan
      // con los de antes, el taller cobraría con el ajuste viejo hasta reabrir la aplicación.
      ref.invalidate(tenantSettingsProvider);
      ref.invalidate(taxRateProvider);
      ref.invalidate(defaultWarrantyDaysProvider);

      if (mounted) _avisar('Ajustes guardados.');
    } catch (e) {
      if (mounted) _avisar(apiErrorMessage(e, 'No se pudieron guardar los ajustes.'));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _avisar(String texto) =>
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(texto)));

  static String? _vacioEsNulo(String s) => s.trim().isEmpty ? null : s.trim();

  @override
  Widget build(BuildContext context) {
    final ajustes = ref.watch(tenantSettingsProvider);

    if (!_cargada && ajustes.hasValue) {
      _llenar(ajustes.requireValue);
      _cargada = true;
    }

    return Scaffold(
      appBar: AppBar(title: const Text('Taller')),
      body: switch (ajustes) {
        AsyncData(:final value) => _formulario(value),
        AsyncError(:final error) => GarajError(
            message: apiErrorMessage(error, 'No se pudieron cargar los ajustes.'),
            onRetry: () => ref.invalidate(tenantSettingsProvider),
          ),
        _ => const GarajSkeletonList(rows: 3),
      },
    );
  }

  Widget _formulario(TenantSettings actual) {
    final theme = Theme.of(context);

    return ListView(
      padding: const EdgeInsets.fromLTRB(
        GarajSpace.md,
        GarajSpace.md,
        GarajSpace.md,
        GarajSpace.xl,
      ),
      children: [
        _Grupo(
          titulo: 'IDENTIDAD DEL TALLER',
          ayuda: 'Es lo que se imprime en cada cotización y en cada factura.',
          children: [
            _campo(_nombre, 'Nombre comercial', capitalizar: true),
            _campo(_razonSocial, 'Razón social', capitalizar: true),
            _campo(_rtn, 'RTN', teclado: TextInputType.number),
            Row(
              children: [
                Expanded(
                  flex: 2,
                  child: _campo(_telefono, 'Teléfono', teclado: TextInputType.phone),
                ),
                const SizedBox(width: GarajSpace.sm),
                Expanded(
                  child: _campo(_codigoPais, 'País', teclado: TextInputType.number),
                ),
              ],
            ),
            _campo(_correo, 'Correo', teclado: TextInputType.emailAddress),
            _campo(_direccion, 'Dirección', capitalizar: true),
          ],
        ),

        _Grupo(
          titulo: 'CÓMO COBRA EL TALLER',
          ayuda: 'Lo que el sistema propone al cotizar y al facturar. Todo se puede cambiar '
              'trabajo por trabajo.',
          children: [
            _campo(
              _isv,
              'ISV (%)',
              teclado: const TextInputType.numberWithOptions(decimal: true),
              ayuda: 'Sus precios ya llevan el impuesto dentro: esto no sube el total, solo '
                  'lo desglosa. En cero no se desglosa nada.',
            ),
            _campo(
              _garantia,
              'Garantía por defecto (días)',
              teclado: TextInputType.number,
              ayuda: 'La que lleva un trabajo al facturarlo. Cero es sin garantía.',
            ),
            SwitchListTile(
              contentPadding: EdgeInsets.zero,
              value: _cobraBodegaje,
              title: const Text('Cobrar bodegaje'),
              subtitle: const Text('Por el vehículo que nadie retira después del aviso.'),
              onChanged: (v) => setState(() => _cobraBodegaje = v),
            ),
            if (_cobraBodegaje)
              Padding(
                padding: const EdgeInsets.only(left: GarajSpace.md),
                child: Row(
                  children: [
                    Expanded(
                      child: _campo(
                        _diasGracia,
                        'Días de gracia',
                        teclado: TextInputType.number,
                      ),
                    ),
                    const SizedBox(width: GarajSpace.sm),
                    Expanded(
                      child: _campo(
                        _porDia,
                        'Por día',
                        teclado: const TextInputType.numberWithOptions(decimal: true),
                      ),
                    ),
                  ],
                ),
              ),
          ],
        ),

        _Grupo(
          titulo: 'QUÉ VE EL TÉCNICO',
          children: [
            SwitchListTile(
              contentPadding: EdgeInsets.zero,
              value: _tecnicoVePrecios,
              title: const Text('El técnico ve precios y totales'),
              onChanged: (v) => setState(() => _tecnicoVePrecios = v),
            ),
            Text(
              'Apagado, el técnico recibe la orden, agrega pasos y carga repuestos, pero no ve '
              'cuánto cuestan ni cuánto suma la orden, y no puede mandarle la cotización al '
              'cliente: le reporta a usted.',
              style: theme.textTheme.bodySmall?.copyWith(color: theme.hintColor),
            ),
          ],
        ),

        const SizedBox(height: GarajSpace.sm),
        FilledButton(
          onPressed: _busy ? null : () => _guardar(actual),
          child: Text(_busy ? 'Guardando…' : 'Guardar'),
        ),
        const SizedBox(height: GarajSpace.md),
        Text(
          'El logo y los rangos de facturación del SAR se configuran en el panel: se hacen una '
          'vez y piden un archivo y los papeles a mano.',
          style: theme.textTheme.bodySmall?.copyWith(color: theme.hintColor),
        ),
      ],
    );
  }

  Widget _campo(
    TextEditingController controlador,
    String etiqueta, {
    TextInputType? teclado,
    bool capitalizar = false,
    String? ayuda,
  }) =>
      Padding(
        padding: const EdgeInsets.only(bottom: GarajSpace.sm),
        child: TextField(
          controller: controlador,
          keyboardType: teclado,
          textCapitalization:
              capitalizar ? TextCapitalization.words : TextCapitalization.none,
          decoration: InputDecoration(
            labelText: etiqueta,
            helperText: ayuda,
            helperMaxLines: 3,
            border: const OutlineInputBorder(),
          ),
        ),
      );
}

class _Grupo extends StatelessWidget {
  const _Grupo({required this.titulo, this.ayuda, required this.children});

  final String titulo;
  final String? ayuda;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.only(bottom: GarajSpace.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(titulo, style: theme.textTheme.labelSmall),
          if (ayuda != null) ...[
            const SizedBox(height: GarajSpace.xs),
            Text(
              ayuda!,
              style: theme.textTheme.bodySmall?.copyWith(color: theme.hintColor),
            ),
          ],
          const SizedBox(height: GarajSpace.sm),
          ...children,
        ],
      ),
    );
  }
}
