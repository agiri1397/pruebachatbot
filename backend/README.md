# ChatbotDocs.Api — Backend .NET 8 (AnythingLLM + Ollama)

Backend de ejemplo en **.NET 8 Web API** que sirve de puente entre un frontend
(Angular) y **AnythingLLM**, que a su vez usa **Ollama** como motor de LLM y de
embeddings. Permite:

- Chatear haciendo preguntas sobre documentos ya cargados en el servidor.
- Subir documentos nuevos y dejarlos listos para ser consultados.
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

## 5. Endpoints

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
Lista los documentos que hay en el servidor (subidos y generados), indicando
si ya están embebidos (disponibles para preguntas) en AnythingLLM.

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

## 6. Consumo desde Angular

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

## 7. Estructura del proyecto

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
    │   └── DocumentStorageService.cs # Documentos guardados en el propio servidor
    ├── Models/                       # DTOs públicos + contratos internos de AnythingLLM
    ├── Options/AnythingLlmOptions.cs
    ├── Storage/                      # Uploads/ y Generated/ (contenido en runtime, no versionado)
    └── Program.cs
```

## Notas

- Las rutas exactas de la Developer API de AnythingLLM pueden variar entre
  versiones; verifícalas en `http://localhost:3001/api/docs` (Swagger propio
  de AnythingLLM) si actualizas la imagen de Docker.
- Este backend no implementa autenticación de usuarios (fuera del alcance del
  ejemplo); en un entorno real conviene añadir autenticación/autorización
  antes de exponerlo públicamente.
