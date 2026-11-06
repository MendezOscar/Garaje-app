//
//  ShareViewController.swift
//  Share Extension
//
//  Lo que hace que GarajApp salga en «Compartir con…» del iPhone. No dibuja nada: recibe el
//  PDF —el informe del escáner—, lo deja en el contenedor que comparten la extensión y la
//  app, y abre GarajApp, que es donde se pregunta a qué orden va.
//
//  Si al compilar sale «No such module 'receive_sharing_intent'»: en Build Phases del target
//  Runner, «Embed Foundation Extensions» tiene que ir antes que «Thin Binary».
//
import receive_sharing_intent

class ShareViewController: RSIShareViewController {
}
