using System.Text.RegularExpressions;

namespace ChatbotDocs.Api.Services;

/// <summary>
/// Convierte el nombre de una carpeta/caso (recibido en la ruta de la API) en un slug seguro
/// (solo a-z, 0-9 y guiones) para usarlo como nombre de subdirectorio y como nombre de
/// workspace en AnythingLLM, evitando path traversal y caracteres inválidos.
/// </summary>
public static class SlugHelper
{
    private static readonly Regex InvalidChars = new("[^a-z0-9-]", RegexOptions.Compiled);
    private static readonly Regex RepeatedDashes = new("-{2,}", RegexOptions.Compiled);

    public static string Sanitize(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '-').Replace('_', '-');
        var slug = RepeatedDashes.Replace(InvalidChars.Replace(normalized, "-"), "-").Trim('-');

        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("El nombre de carpeta/caso no es válido.", nameof(value));
        }

        return slug;
    }
}
