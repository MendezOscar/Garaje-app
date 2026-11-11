import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:image_picker/image_picker.dart';

import '../../core/api/api_client.dart';
import '../../core/api/assembly_repository.dart';
import '../../core/models/assembly.dart';
import '../../core/models/media.dart';
import '../../core/sync/upload_queue.dart';
import '../work_orders/photo_capture.dart';
import 'escanear_etiqueta_screen.dart';

/// «Armé una moto»: el registro en la bodega, con la moto delante.
///
/// Es la pantalla que reemplaza la foto al grupo de WhatsApp y el Excel de la noche. Dos
/// decisiones que la gobiernan:
///
/// No se sale al registrar. El técnico arma varias seguidas, así que al guardar se limpia lo
/// que cambia —la serie y la foto— y se conserva lo que no —contratista, modelo y bodega—,
/// que es lo mismo toda la tarde.
///
/// Y si el envío falla, **no se borra nada**. Lo tecleado se queda en pantalla y se reintenta.
/// Perder una serie ya escrita es lo que manda a cualquiera de vuelta al WhatsApp.
class RegisterMotorcycleScreen extends ConsumerStatefulWidget {
  const RegisterMotorcycleScreen({super.key});

  @override
  ConsumerState<RegisterMotorcycleScreen> createState() => _RegisterMotorcycleScreenState();
}

class _RegisterMotorcycleScreenState extends ConsumerState<RegisterMotorcycleScreen> {
  final _serie = TextEditingController();
  final _notas = TextEditingController();

  String? _contratista;
  String? _modelo;
  String? _bodega;

  /// A quién se le apunta. Null es «a mí», que es lo único que puede el técnico.
  String? _tecnico;

  File? _foto;
  bool _guardando = false;
  String? _error;

  /// Lo que lleva registrado en esta sesión de la pantalla. Es el acuse que vale: el técnico
  /// quiere saber que la anterior entró antes de empezar la siguiente.
  final _registradas = <MotorcycleAssembly>[];

  /// La propuesta de modelo que sale de las series ya registradas.
  ModelGuess? _propuesta;
  Timer? _rebote;

  @override
  void dispose() {
    _rebote?.cancel();
    _serie.dispose();
    _notas.dispose();
    super.dispose();
  }

  /// El estándar son 17 caracteres y no usa I, O ni Q, que se confunden con 1 y 0.
  ///
  /// Aviso y no bloqueo: hay chasis que no lo siguen, y negarse a guardar el número que está
  /// en la moto sería peor que guardarlo raro.
  String? get _avisoDeLaSerie {
    final v = _limpia;
    if (v.isEmpty) return null;
    if (v.length != 17) return 'Son ${v.length} caracteres; una serie estándar lleva 17.';
    return RegExp('[IOQ]').hasMatch(v)
        ? 'Una serie no lleva I, O ni Q: revise si son 1, 0 o 9.'
        : null;
  }

  String get _limpia => _serie.text.replaceAll(RegExp('[^A-Za-z0-9]'), '').toUpperCase();

  /// Pregunta qué modelo suele llevar una serie así. Con rebote: se teclea carácter a
  /// carácter y no hay por qué preguntar diecisiete veces.
  void _alTeclearLaSerie() {
    setState(() {});
    _rebote?.cancel();

    if (_limpia.length < 8) {
      if (_propuesta != null) setState(() => _propuesta = null);
      return;
    }

    _rebote = Timer(const Duration(milliseconds: 400), () async {
      try {
        final guess = await ref.read(assemblyRepositoryProvider).guessModel(_limpia);
        if (!mounted) return;

        setState(() {
          _propuesta = guess.modelId == null ? null : guess;
          // Se propone, no se impone: si todavía no eligió modelo, se le adelanta; si ya
          // eligió, no se le cambia debajo de la mano.
          _modelo ??= guess.modelId;
        });
      } catch (_) {
        // Que la propuesta no se pueda calcular no es un error que detenga nada.
      }
    });
  }

  Future<void> _escanear() async {
    final serie = await Navigator.of(context).push<String>(
      MaterialPageRoute(builder: (_) => const EscanearEtiquetaScreen()),
    );

    if (serie == null || !mounted) return;

    // Cae en el campo, no se guarda: la confirma quien tiene la caja delante. Y al escribirla
    // se dispara la propuesta de modelo, igual que si la hubiera tecleado.
    _serie.text = serie;
    _alTeclearLaSerie();
  }

  Future<void> _tomarFoto() async {
    final picked = await ImagePicker().pickImage(
      source: ImageSource.camera,
      maxWidth: 1600,
      maxHeight: 1600,
      imageQuality: 85,
    );

    if (picked == null) return;

    final comprimida = await comprimirFoto(File(picked.path));
    if (!mounted) return;

    setState(() => _foto = comprimida);
  }

  Future<void> _registrar() async {
    if (_modelo == null || _bodega == null || _contratista == null) return;
    if (_limpia.length < 6) {
      setState(() => _error = 'La serie está muy corta. Cópiela completa del chasis.');
      return;
    }

    setState(() {
      _guardando = true;
      _error = null;
    });

    try {
      final moto = await ref.read(assemblyRepositoryProvider).register(
            serial: _limpia,
            modelId: _modelo!,
            contractorId: _contratista!,
            warehouseId: _bodega!,
            notes: _notas.text.trim().isEmpty ? null : _notas.text.trim(),
            technicianUserId: _tecnico,
          );

      // La foto se encola después, cuando la moto ya tiene id. La cola aguanta sin señal:
      // la moto queda registrada aunque la foto suba más tarde.
      if (_foto != null) {
        await ref.read(uploadQueueProvider.notifier).enqueue(
              photo: _foto!,
              ownerType: MediaOwnerType.motorcycleAssembly,
              ownerId: moto.id,
              takenAt: DateTime.now(),
            );
      }

      ref.invalidate(myAssembliesProvider);
      if (!mounted) return;

      setState(() {
        _registradas.insert(0, moto);
        // Se limpia lo que cambia de una moto a otra. El contratista, el modelo y la bodega
        // se quedan: son los mismos toda la tarde.
        _serie.clear();
        _notas.clear();
        _foto = null;
        _propuesta = null;
      });

      FocusScope.of(context).requestFocus(FocusNode());
    } catch (e) {
      // No se borra nada: lo tecleado sigue en pantalla para reintentar. Perder una serie ya
      // escrita es lo que manda a cualquiera de vuelta al WhatsApp.
      if (mounted) setState(() => _error = apiErrorMessage(e));
    } finally {
      if (mounted) setState(() => _guardando = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final listas = ref.watch(assemblyPickListsProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Armé una moto')),
      body: listas.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Text(apiErrorMessage(e), textAlign: TextAlign.center),
          ),
        ),
        data: (datos) {
          if (!datos.listo) {
            return const Center(
              child: Padding(
                padding: EdgeInsets.all(24),
                child: Text(
                  'Falta el contrato: el dueño tiene que cargar el contratista, los modelos '
                  'y las bodegas antes de que se pueda registrar.',
                  textAlign: TextAlign.center,
                ),
              ),
            );
          }

          // Con un solo contratista no se pregunta: en esta pantalla cada toque de más es
          // una moto que termina anotada en el WhatsApp.
          _contratista ??= datos.contractors.length == 1 ? datos.contractors.first.id : null;
          _bodega ??= datos.warehouses.length == 1 ? datos.warehouses.first.id : null;

          // Con un solo técnico no se pregunta. Con varios se elige a mano y no hay
          // preseleccionado: el dueño no arma motos, así que no hay un «yo» razonable, y
          // equivocarse de técnico es un pago que se le va al que no la armó.
          if (datos.technicians.length == 1) _tecnico ??= datos.technicians.first.id;

          return ListView(
            padding: const EdgeInsets.fromLTRB(16, 16, 16, 32),
            children: [
              // Escanear primero: el VIN viene en código de barras en la etiqueta de la
              // caja, y leerlo de ahí no se equivoca como se equivoca alguien tecleando
              // diecisiete caracteres. Teclear sigue estando, para la etiqueta rota.
              OutlinedButton.icon(
                onPressed: _escanear,
                icon: const Icon(Icons.qr_code_scanner),
                style: OutlinedButton.styleFrom(minimumSize: const Size.fromHeight(48)),
                label: const Text('Escanear la etiqueta de la caja'),
              ),
              const SizedBox(height: 12),

              TextField(
                controller: _serie,
                autocorrect: false,
                textCapitalization: TextCapitalization.characters,
                maxLength: 25,
                style: const TextStyle(fontFamily: 'monospace', letterSpacing: 1.2),
                inputFormatters: [UpperCaseTextFormatter()],
                decoration: InputDecoration(
                  labelText: 'Serie del chasis',
                  helperText: _avisoDeLaSerie ?? 'Sin guiones ni espacios.',
                  helperMaxLines: 2,
                  prefixIcon: const Icon(Icons.tag),
                ),
                onChanged: (_) => _alTeclearLaSerie(),
              ),

              if (_propuesta case final p? when p.modelName != null) ...[
                const SizedBox(height: 4),
                Row(
                  children: [
                    const Icon(Icons.lightbulb_outline, size: 16),
                    const SizedBox(width: 6),
                    Expanded(
                      child: Text(
                        // Sin veces es porque el prefijo lo dejó escrito el dueño en el
                        // catálogo; con veces, porque se aprendió de lo ya registrado. Son
                        // dos cosas distintas y conviene que se note cuál es.
                        p.seenBefore == 0
                            ? 'Por el inicio de la serie, es una ${p.modelName}. '
                                'Confírmelo con la moto.'
                            : 'Series como esta han sido ${p.modelName} '
                                '${p.seenBefore == 1 ? 'una vez' : '${p.seenBefore} veces'}. '
                                'Confírmelo con la moto.',
                        style: theme.textTheme.bodySmall,
                      ),
                    ),
                  ],
                ),
              ],

              const SizedBox(height: 16),
              _Elegir(
                etiqueta: 'Modelo',
                icono: Icons.two_wheeler,
                valor: _modelo,
                opciones: datos.models,
                onChanged: (v) => setState(() => _modelo = v),
              ),
              const SizedBox(height: 12),
              _Elegir(
                etiqueta: 'Bodega',
                icono: Icons.warehouse_outlined,
                valor: _bodega,
                opciones: datos.warehouses,
                onChanged: (v) => setState(() => _bodega = v),
              ),

              // A quién se le apunta. Solo lo ve quien manda: al técnico le llega vacía y
              // el servidor le apunta lo suyo pase lo que pase. Es el caso del dueño que
              // sigue recibiendo las fotos por WhatsApp y registra él lo que armó otro.
              if (datos.technicians.isNotEmpty) ...[
                const SizedBox(height: 12),
                _Elegir(
                  etiqueta: 'La armó',
                  icono: Icons.person_outline,
                  valor: _tecnico,
                  opciones: datos.technicians,
                  onChanged: (v) => setState(() => _tecnico = v),
                ),
              ],

              // Con uno solo no se enseña: ya quedó elegido y preguntarlo sería ruido.
              if (datos.contractors.length > 1) ...[
                const SizedBox(height: 12),
                _Elegir(
                  etiqueta: 'Contratista',
                  icono: Icons.handshake_outlined,
                  valor: _contratista,
                  opciones: datos.contractors,
                  onChanged: (v) => setState(() => _contratista = v),
                ),
              ],

              const SizedBox(height: 12),
              TextField(
                controller: _notas,
                maxLength: 500,
                maxLines: 2,
                textCapitalization: TextCapitalization.sentences,
                decoration: const InputDecoration(
                  labelText: 'Nota (opcional)',
                  hintText: 'Algo que el dueño tenga que saber de esta moto',
                ),
              ),

              const SizedBox(height: 4),
              _Foto(foto: _foto, onTomar: _tomarFoto, onQuitar: () => setState(() => _foto = null)),

              if (_error != null) ...[
                const SizedBox(height: 8),
                Container(
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(
                    color: theme.colorScheme.errorContainer,
                    borderRadius: BorderRadius.circular(8),
                  ),
                  child: Text(
                    _error!,
                    style: TextStyle(color: theme.colorScheme.onErrorContainer),
                  ),
                ),
              ],

              const SizedBox(height: 16),
              FilledButton.icon(
                onPressed: _guardando ||
                        _limpia.isEmpty ||
                        _modelo == null ||
                        _bodega == null ||
                        // Lista llena quiere decir que quien está dentro no arma motos: hasta
                        // que diga quién la armó, no hay a quién pagarle.
                        (datos.technicians.isNotEmpty && _tecnico == null)
                    ? null
                    : _registrar,
                icon: _guardando
                    ? const SizedBox(
                        width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2))
                    : const Icon(Icons.check),
                style: FilledButton.styleFrom(
                  minimumSize: const Size.fromHeight(52),
                  textStyle: const TextStyle(fontSize: 16, fontWeight: FontWeight.w600),
                ),
                label: Text(_guardando ? 'Registrando…' : 'Registrar la moto'),
              ),

              if (_registradas.isNotEmpty) ...[
                const SizedBox(height: 24),
                Text(
                  'Registradas ahorita (${_registradas.length})',
                  style: theme.textTheme.titleSmall,
                ),
                const SizedBox(height: 8),
                for (final m in _registradas)
                  ListTile(
                    contentPadding: EdgeInsets.zero,
                    dense: true,
                    leading: const Icon(Icons.check_circle_outline, color: Colors.green),
                    title: Text(
                      m.serial,
                      style: const TextStyle(fontFamily: 'monospace'),
                    ),
                    subtitle: Text('${m.modelName} · ${m.warehouseName}'),
                    trailing: m.technicianPay == null
                        ? null
                        : Text('L ${m.technicianPay!.toStringAsFixed(2)}'),
                  ),
              ],
            ],
          );
        },
      ),
    );
  }
}

/// Mayúsculas al teclear: una serie se compara letra por letra con el chasis.
class UpperCaseTextFormatter extends TextInputFormatter {
  @override
  TextEditingValue formatEditUpdate(TextEditingValue anterior, TextEditingValue nuevo) =>
      nuevo.copyWith(text: nuevo.text.toUpperCase());
}

/// La foto de la etiqueta: opcional y callada.
///
/// Antes era el primer paso y la pantalla la pedía, porque la foto hacía dos trabajos:
/// llevarle la serie al dueño —que la leía del grupo de WhatsApp y la tecleaba— y servir de
/// constancia. El código de barras se quedó con el primero, que era el pesado.
///
/// Queda el segundo, que importa el día que el contratista discuta una factura: es lo único
/// que acredita que alguien estuvo frente a esa caja. Así que sigue aquí, un renglón, sin
/// pedirla y sin reprochar que falte.
class _Foto extends StatelessWidget {
  const _Foto({required this.foto, required this.onTomar, required this.onQuitar});

  final File? foto;
  final VoidCallback onTomar;
  final VoidCallback onQuitar;

  @override
  Widget build(BuildContext context) {
    if (foto == null) {
      return Align(
        alignment: Alignment.centerLeft,
        child: TextButton.icon(
          onPressed: onTomar,
          icon: const Icon(Icons.photo_camera_outlined, size: 18),
          label: const Text('Agregar una foto de la etiqueta'),
        ),
      );
    }

    return Row(
      children: [
        ClipRRect(
          borderRadius: BorderRadius.circular(8),
          child: Image.file(foto!, height: 56, width: 56, fit: BoxFit.cover),
        ),
        const SizedBox(width: 12),
        const Expanded(child: Text('Foto de la etiqueta')),
        TextButton(onPressed: onQuitar, child: const Text('Quitar')),
      ],
    );
  }
}

/// Un desplegable de una de las tres listas.
class _Elegir extends StatelessWidget {
  const _Elegir({
    required this.etiqueta,
    required this.icono,
    required this.valor,
    required this.opciones,
    required this.onChanged,
  });

  final String etiqueta;
  final IconData icono;
  final String? valor;
  final List<AssemblyName> opciones;
  final ValueChanged<String?> onChanged;

  @override
  Widget build(BuildContext context) => DropdownButtonFormField<String>(
        initialValue: valor,
        isExpanded: true,
        decoration: InputDecoration(labelText: etiqueta, prefixIcon: Icon(icono)),
        items: [
          for (final o in opciones) DropdownMenuItem(value: o.id, child: Text(o.name)),
        ],
        onChanged: onChanged,
      );
}
