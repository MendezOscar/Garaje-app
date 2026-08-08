import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'core/router/app_router.dart';
import 'core/theme/garaj_brand.dart';

void main() {
  runApp(const ProviderScope(child: GarajApp()));
}

class GarajApp extends ConsumerWidget {
  const GarajApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final router = ref.watch(appRouterProvider);

    return MaterialApp.router(
      title: 'GarajApp',
      debugShowCheckedModeBanner: false,
      routerConfig: router,
      theme: garajTheme,
      darkTheme: garajDarkTheme,
    );
  }
}
