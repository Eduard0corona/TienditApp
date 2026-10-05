namespace FoodParkDigital.Application.Messaging;

public static class MessagingDefaults
{
    /// <summary>Used while the business has not chosen a reminder template.</summary>
    public const string ReminderBody =
        "Hola {nombre}, te recordamos tu cita de {servicio} el {fecha} a las {hora} en {negocio}. "
        + "Si no puedes venir, respóndenos este mensaje. Escribe BAJA si no quieres recibir recordatorios.";

    public const string OptOutReply =
        "Listo, ya no te enviaremos promociones ni recordatorios. Seguirás recibiendo los avisos de tus pedidos y citas. "
        + "Escribe ALTA si quieres volver a recibirlos.";

    public const string OptInReply =
        "¡Listo! Volverás a recibir nuestras promociones y recordatorios. Escribe BAJA cuando ya no quieras recibirlos.";

    public const string WhatsAppNotConfigured = "Conecta WhatsApp en Mi negocio antes de enviar mensajes.";
}
