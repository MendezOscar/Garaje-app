/// Una consulta del técnico sobre la orden.
class ConversationListItem {
  const ConversationListItem({
    required this.id,
    required this.title,
    required this.isClosed,
    required this.turns,
    required this.createdAt,
  });

  factory ConversationListItem.fromJson(Map<String, dynamic> json) => ConversationListItem(
        id: json['id'] as String,
        title: json['title'] as String? ?? '',
        isClosed: json['isClosed'] as bool? ?? false,
        turns: (json['turns'] as num?)?.toInt() ?? 0,
        createdAt: DateTime.parse(json['createdAt'] as String),
      );

  final String id;
  final String title;
  final bool isClosed;

  /// Cuántas preguntas lleva. Se cierra a las seis.
  final int turns;
  final DateTime createdAt;
}

class Conversation {
  const Conversation({
    required this.id,
    required this.title,
    required this.isClosed,
    required this.turns,
    required this.message,
    required this.creditsLeftToday,
  });

  factory Conversation.fromJson(Map<String, dynamic> json) => Conversation(
        id: json['id'] as String?,
        title: json['title'] as String?,
        isClosed: json['isClosed'] as bool? ?? false,
        turns: ((json['turns'] as List<dynamic>?) ?? [])
            .map((e) => ConversationTurn.fromJson(e as Map<String, dynamic>))
            .toList(),
        message: json['message'] as String?,
        creditsLeftToday: (json['creditsLeftToday'] as num?)?.toInt() ?? 0,
      );

  /// Null cuando no llegó a abrirse: entonces manda [message].
  final String? id;
  final String? title;
  final bool isClosed;
  final List<ConversationTurn> turns;

  /// Por qué no se pudo preguntar. Nunca es un error que haya que pintar como tal.
  final String? message;

  /// Una consulta vale 5 créditos, así que quien pregunta tiene que poder decidir si gasta.
  final int creditsLeftToday;
}

class ConversationTurn {
  const ConversationTurn({
    required this.fromUser,
    required this.content,
    required this.at,
  });

  factory ConversationTurn.fromJson(Map<String, dynamic> json) => ConversationTurn(
        fromUser: json['fromUser'] as bool? ?? false,
        content: json['content'] as String? ?? '',
        at: DateTime.parse(json['at'] as String),
      );

  final bool fromUser;
  final String content;
  final DateTime at;
}
