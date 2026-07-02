# ChatbotDocs.Api — Backend .NET 8 (AnythingLLM + Ollama)

Backend de ejemplo en **.NET 8 Web API** que sirve de puente entre un frontend
(Angular) y **AnythingLLM**, que a su vez usa **Ollama** como motor de LLM y de
embeddings. Permite:

- Chatear haciendo preguntas sobre documentos ya cargados en el servidor.
- Colocar documentos en una **carpeta fija del servidor** (`Documentos/`) y que
  se sincronicen automáticamente para poder cruzarlos en una sola respuesta
  (p. ej. "según el marco legal y los formularios ingresados, ¿la
  municipalidad está preparada para una asociación público-privada?").
- Subir documentos nuevos por API y dejarlos listos para ser consultados.
- Generar documentos nuevos con el modelo y descargarlos.

```
Angular (frontend)  --HTTP-->  ChatbotDocs.Api (.NET 8)  --HTTP-->  AnythingLLM  --HTTP-->  Ollama
                                        |
                                        v
                              Storage/ (documentos en el propio servidor)
```

AnythingLLM es quien hace el trabajo pesado de RAG (extraer texto, trocear,
generar embeddings y buscar contexto relevante); Ollama es el motor que
ejecuta el modelo local (p. ej. `llama3.1`). El backend .NET no reimplementa
nada de eso: expone una API sencilla y propia para el frontend, y delega en
AnythingLLM.

## 1. Levantar Ollama + AnythingLLM

```bash
cd backend
docker compose up -d
```

Esto arranca:
- `ollama` en `http://localhost:11434`
- `anythingllm` en `http://localhost:3001`

Descarga un modelo dentro del contenedor de Ollama (una sola vez):

```bash
docker exec -it ollama ollama pull llama3.1
```

## 2. Configurar AnythingLLM (una sola vez, vía su UI)

1. Abre `http://localhost:3001` en el navegador y completa el asistente inicial.
2. En **LLM Preference**, elige *Ollama* y el modelo que descargaste (`llama3.1`).
3. En **Embedding Preference**, elige también *Ollama*.
4. Crea un workspace, por ejemplo con slug `chatbot-docs` (Settings → Workspace → el slug aparece en la URL, `http://localhost:3001/workspace/chatbot-docs`).
5. Ve a **Settings → Developer API** y genera una **API Key**.

## 3. Configurar el backend

Edita `ChatbotDocs.Api/appsettings.json` (o mejor, usa `appsettings.Development.json` /
variables de entorno para no commitear la clave real):

```json
{
  "AnythingLlm": {
    "BaseUrl": "http://localhost:3001",
    "ApiKey": "TU_API_KEY_DE_ANYTHINGLLM",
    "WorkspaceSlug": "chatbot-docs",
    "DocumentFolder": "custom-documents"
  }
}
```

También puedes usar variables de entorno (útil en Docker/CI):

```bash
export AnythingLlm__ApiKey="TU_API_KEY"
export AnythingLlm__WorkspaceSlug="chatbot-docs"
```

## 4. Ejecutar el backend

```bash
cd backend/ChatbotDocs.Api
dotnet restore
dotnet run
```

Por defecto queda escuchando en `http://localhost:5000` (revisa la consola por
si Kestrel elige otro puerto) y expone Swagger en `/swagger` en entorno
Development. CORS está habilitado para `http://localhost:4200` (Angular dev
server); puedes añadir más orígenes en `Cors:AllowedOrigins`.

## 5. Carpeta fija de documentos (marco legal, formularios, etc.)

Para el caso de uso de "cruzar varios documentos completos en una sola
respuesta" (ej. marco legal + formularios ingresados), no hace falta subir
archivo por archivo desde el frontend: simplemente colócalos dentro de
`ChatbotDocs.Api/Documentos/` (puedes organizarlos en subcarpetas libremente,
por ejemplo `Documentos/MarcoLegal/` y `Documentos/Formularios/` — la
estructura de carpetas es solo para tu propia organización, todo termina en
el mismo workspace).

```bash
mkdir -p backend/ChatbotDocs.Api/Documentos/MarcoLegal
mkdir -p backend/ChatbotDocs.Api/Documentos/Formularios
cp ley-municipal.pdf backend/ChatbotDocs.Api/Documentos/MarcoLegal/
cp formulario-2024.pdf backend/ChatbotDocs.Api/Documentos/Formularios/
```

Al iniciar (`dotnet run`), el backend escanea esa carpeta automáticamente y,
por cada archivo nuevo:
1. Lo sube a AnythingLLM y lo **embebe** (queda buscable por similitud semántica).
2. Lo **fija ("pin")** en el workspace, para que su contenido completo se
   incluya en cada respuesta del chat, en vez de depender solo de fragmentos
   encontrados por búsqueda semántica. Esto es lo que permite que una
   pregunta como *"según el marco legal y los formularios ingresados, ¿la
   municipalidad está preparada para una asociación público-privada?"* se
   responda considerando el contenido íntegro de todos los documentos
   relevantes, no solo los trozos más parecidos a la pregunta.

Si agregas o quitas archivos de la carpeta mientras el backend ya está
corriendo, llama a `POST /api/documents/sync` para volver a sincronizar sin
reiniciar.

> Ten en cuenta que fijar ("pin") muchos documentos grandes hace que cada
> consulta le mande al modelo todo ese contenido como contexto — si usas un
> modelo con ventana de contexto chica en Ollama, o tienes decenas de
> documentos extensos, puede que necesites un modelo con más contexto o
> reducir cuántos documentos fijas.

## 6. Endpoints

### `POST /api/chat`
Envía un mensaje y responde usando el contexto de los documentos embebidos.

```json
// Request
{ "message": "¿Qué dice la política de vacaciones?", "mode": "query" }

// Response
{
  "answer": "Según el documento...",
  "sources": [ { "title": "politica-vacaciones.pdf", "excerpt": "..." } ]
}
```

`mode` puede ser:
- `"query"`: solo responde si encuentra contexto relevante en los documentos.
- `"chat"`: responde también con conocimiento general del modelo si no hay contexto.

### `GET /api/documents`
Lista los documentos que hay en el servidor (de la carpeta fija, subidos por
API, y generados), indicando si ya están embebidos (disponibles para
preguntas) en AnythingLLM.

### `POST /api/documents/sync`
Vuelve a escanear la carpeta fija (`Documentos/`) y embebe + fija ("pin") los
archivos nuevos que encuentre. Se ejecuta automáticamente al iniciar el
backend; llama a este endpoint si agregaste archivos después.

```json
// Response
{
  "newlyEmbedded": ["ley-municipal.pdf", "formulario-2024.pdf"],
  "alreadyEmbedded": [],
  "errors": []
}
```

### `POST /api/documents/upload` (multipart/form-data, campo `file`)
Guarda el archivo en el servidor (`Storage/Uploads`) y lo sube + embebe en el
workspace de AnythingLLM para poder preguntarle sobre su contenido.

Extensiones soportadas: `.pdf .txt .md .docx .csv .json`.

### `POST /api/documents/generate`
Pide al modelo que redacte un documento nuevo.

```json
// Request
{
  "prompt": "Resumen ejecutivo de la reunión de arquitectura de hoy",
  "fileName": "resumen-reunion",
  "embedAfterGeneration": true
}

// Response
{
  "fileName": "resumen-reunion.md",
  "content": "# Resumen ejecutivo\n...",
  "embeddedInWorkspace": true
}
```

Si `embedAfterGeneration` es `true` (por defecto), el documento generado se
vuelve a subir y embeber, así el chat puede responder preguntas sobre lo que
acaba de generar.

### `GET /api/documents/download/{fileName}`
Descarga un documento (subido o generado) del servidor.

## 7. Consumo desde Angular

Ejemplo mínimo de servicio Angular (`chat.service.ts`):

```typescript
@Injectable({ providedIn: 'root' })
export class ChatService {
  private readonly baseUrl = 'http://localhost:5000/api';

  constructor(private http: HttpClient) {}

  chat(message: string, mode: 'query' | 'chat' = 'query') {
    return this.http.post<{ answer: string; sources: { title: string; excerpt: string }[] }>(
      `${this.baseUrl}/chat`, { message, mode }
    );
  }

  listDocuments() {
    return this.http.get<any[]>(`${this.baseUrl}/documents`);
  }

  uploadDocument(file: File) {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post(`${this.baseUrl}/documents/upload`, formData);
  }

  generateDocument(prompt: string, fileName?: string) {
    return this.http.post(`${this.baseUrl}/documents/generate`, { prompt, fileName });
  }

  downloadUrl(fileName: string) {
    return `${this.baseUrl}/documents/download/${encodeURIComponent(fileName)}`;
  }
}
```

## 8. Estructura del proyecto

```
backend/
├── docker-compose.yml          # Ollama + AnythingLLM
├── ChatbotDocs.sln
└── ChatbotDocs.Api/
    ├── Controllers/
    │   ├── ChatController.cs
    │   └── DocumentsController.cs
    ├── Services/
    │   ├── AnythingLlmClient.cs      # Cliente HTTP hacia la API de AnythingLLM
    │   ├── DocumentStorageService.cs # Documentos subidos/generados por API
    │   └── DocumentSyncService.cs    # Escanea Documentos/ y embebe + fija (pin) en AnythingLLM
    ├── Models/                       # DTOs públicos + contratos internos de AnythingLLM
    ├── Options/
    │   ├── AnythingLlmOptions.cs
    │   └── DocumentSyncOptions.cs
    ├── Documentos/                   # Carpeta fija: coloca aquí marco legal, formularios, etc.
    ├── Storage/                      # Uploads/ y Generated/ (contenido en runtime, no versionado)
    └── Program.cs
```

## Notas

- Las rutas exactas de la Developer API de AnythingLLM (incluyendo
  `update-pin`, usada para fijar documentos) pueden variar entre versiones;
  verifícalas en `http://localhost:3001/api/docs` (Swagger propio de
  AnythingLLM) si actualizas la imagen de Docker. Si tu versión no soporta
  `update-pin` vía API, puedes fijar cada documento manualmente desde la
  interfaz de AnythingLLM (ícono de pin junto al documento dentro del
  workspace) — el backend seguirá funcionando igual para embeber/buscar,
  simplemente no automatiza ese último paso.
- Este backend no implementa autenticación de usuarios (fuera del alcance del
  ejemplo); en un entorno real conviene añadir autenticación/autorización
  antes de exponerlo públicamente.
