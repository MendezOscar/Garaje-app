import type { PlatformTenant } from '@/types/domain'

/**
 * Las dos reglas con que la plataforma mira a un taller como cliente.
 *
 * Viven aquí y no dentro de una pantalla porque las usan tres: la lista de Talleres, que pinta
 * las tarjetas de «Necesitan atención», el menú, que lleva el número en rojo, y cualquiera que
 * venga después. Tenerlas en dos lados era la forma de que el menú dijera 3 y la lista 4.
 */

/**
 * Las cuentas que son nuestras, no de un cliente: las que deja una prueba del servidor
 * («Prueba 3F2A…») y las de demostración —«Taller Demo», «Taller de equipos Demo»—, que se
 * reconocen por llevar «demo» en el nombre.
 *
 * No se les cobra ni se les llama, así que no cuentan para nada: ni para el número del menú,
 * ni para las tarjetas de «Necesitan atención», ni para la lista de a quién llamar.
 */
const DEMO = /^prueba |\bdemo\b/i

export function esDemo(nombre: string): boolean {
  return DEMO.test(nombre)
}

/**
 * Un taller sin cuota no es un cliente que deba: es una cuenta de prueba o de cortesía.
 * Mezclarlo con los que pagan arruina las dos cuentas —la de ingresos y la de mora—, y lo que
 * hay que hacer con él es otra cosa: ponerle plan.
 */
export function esDePrueba(t: PlatformTenant): boolean {
  return t.isActive && t.monthlyFee <= 0
}

/**
 * A quién hay que atender hoy: el que debe y el que está por vencer, más la prueba que se
 * acaba esta semana. El suspendido no cuenta —ya no es trabajo pendiente— ni el que está al
 * día con fecha lejana.
 */
export function necesitaAtencion(t: PlatformTenant): boolean {
  if (!t.isActive) return false

  // Las nuestras no: que a «Taller de equipos Demo» se le acabe la prueba no es trabajo.
  if (esDemo(t.name)) return false

  // La prueba que se termina sí es trabajo: hay que ponerle plan antes de que se le corte.
  if (esDePrueba(t)) return t.paidThrough !== null && (t.daysLeft ?? 99) <= 7

  return t.state !== 'Active'
}
