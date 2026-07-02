namespace ChatbotDocs.Api.Options;

public class DocumentSyncOptions
{
    public const string SectionName = "DocumentSync";

    /// <summary>Carpeta (relativa al content root del backend) donde se colocan manualmente los documentos fuente (marco legal, formularios, etc.).</summary>
    public string FolderPath { get; set; } = "Documentos";

    /// <summary>Si es true, al arrancar el backend se sincroniza automáticamente el contenido de la carpeta con AnythingLLM.</summary>
    public bool AutoSyncOnStartup { get; set; } = true;
}
