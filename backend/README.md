# ChatbotDocs.Api — Backend .NET 8 (AnythingLLM + Ollama)

Backend de ejemplo en **.NET 8 Web API** que sirve de puente entre un frontend
(Angular) y **AnythingLLM**, que a su vez usa **Ollama** como motor de LLM y de
embeddings. Permite:

- Manejar varios **casos independientes** ("carpetas"), cada uno con sus
  propios documentos y su propio workspace en AnythingLLM, sin que se mezclen
  entre sí (ej. un caso por municipalidad, por expediente, por proyecto...).
- Colocar documentos de un caso en su **carpeta fija del servidor**
  (`Documentos/{carpeta}/`) y que se sincronicen automáticamente para poder
  cruzarlos en una sola respuesta (p. ej. "según el marco legal y los
  formularios ingresados, ¿la municipalidad está preparada para una
  asociación público-privada?").
- Subir documentos nuevos por API a un caso y dejarlos listos para consultar.
- Generar documentos nuevos con el modelo (para un caso) y descargarlos.

```
Angular (frontend)  --HTTP-->  ChatbotDocs.Api (.NET 8)  --HTTP-->  AnythingLLM  --HTTP-->  Ollama
                                        |                                |
                                        v                                v
                          Storage/{carpeta} (docs en el servidor)   1 workspace por carpeta
```

AnythingLLM es quien hace el trabajo pesado de RAG (extraer texto, trocear,
generar embeddings y buscar contexto relevante); Ollama es el motor que
ejecuta el modelo local (p. ej. `llama3.1`). El backend .NET no reimplementa
nada de eso: expone una API sencilla y propia para el frontend, y delega en
AnythingLLM. La única pieza propia de orquestación es el mapeo
**1 carpeta = 1 workspace**, que el backend crea automáticamente la primera
vez que se usa una carpeta nueva.

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
4. Ve a **Settings → Developer API** y genera una **API Key**. (No hace falta
   crear workspaces a mano: el backend crea uno automáticamente por cada
   carpeta/caso nuevo que uses).

## 3. Configurar el backend

Edita `ChatbotDocs.Api/appsettings.json` (o mejor, usa `appsettings.Development.json` /
variables de entorno para no commitear la clave real):

```json
{
  "AnythingLlm": {
    "BaseUrl": "http://localhost:3001",
    "ApiKey": "TU_API_KEY_DE_ANYTHINGLLM"
  }
}
```

También puedes usar variables de entorno (útil en Docker/CI):

```bash
export AnythingLlm__ApiKey="TU_API_KEY"
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

## 5. Carpetas/casos y documentos (marco legal, formularios, etc.)

Cada **caso** vive en su propia subcarpeta dentro de
`ChatbotDocs.Api/Documentos/`, y el backend lo mapea 1:1 a su propio workspace
en AnythingLLM (aislado de los demás casos). Dentro de la carpeta de un caso
puedes organizar los documentos como quieras — la estructura de subcarpetas
es solo para tu propia organización, todo termina en el mismo workspace de
ese caso:

```bash
mkdir -p backend/ChatbotDocs.Api/Documentos/municipalidad-san-juan/MarcoLegal
mkdir -p backend/ChatbotDocs.Api/Documentos/municipalidad-san-juan/Formularios
cp ley-municipal.pdf   backend/ChatbotDocs.Api/Documentos/municipalidad-san-juan/MarcoLegal/
cp formulario-2024.pdf backend/ChatbotDocs.Api/Documentos/municipalidad-san-juan/Formularios/
```

El nombre de la subcarpeta (`municipalidad-san-juan` en el ejemplo) es el
identificador del caso: es el mismo valor que usarás como `{carpeta}` en
todas las rutas de la API descritas más abajo.

Al iniciar (`dotnet run`), el backend recorre todas las carpetas que
encuentre dentro de `Documentos/` y, por cada archivo nuevo dentro de cada
una:
1. Crea el workspace del caso en AnythingLLM si todavía no existe.
2. Sube el archivo y lo **embebe** (queda buscable por similitud semántica).
3. Lo **fija ("pin")** en ese workspace, para que su contenido completo se
   incluya en cada respuesta del chat de ese caso, en vez de depender solo de
   fragmentos encontrados por búsqueda semántica. Esto es lo que permite que
   una pregunta como *"según el marco legal y los formularios ingresados,
   ¿la municipalidad está preparada para una asociación público-privada?"*
   se responda considerando el contenido íntegro de los documentos de ese
   caso, no solo los trozos más parecidos a la pregunta.

Si agregas o quitas archivos de la carpeta de un caso mientras el backend ya
está corriendo, llama a `POST /api/documents/{carpeta}/sync` para volver a
sincronizar sin reiniciar.

> Ten en cuenta que fijar ("pin") muchos documentos grandes hace que cada
> consulta le mande al modelo todo ese contenido como contexto — si usas un
> modelo con ventana de contexto chica en Ollama, o un caso tiene decenas de
> documentos extensos, puede que necesites un modelo con más contexto o
> reducir cuántos documentos fijas en ese caso.

## 6. Endpoints

Todas las rutas de chat y documentos reciben `{carpeta}` como parte de la
ruta: es el identificador del caso (el mismo nombre que la subcarpeta dentro
de `Documentos/`, o cualquier nombre nuevo si el caso solo va a tener
documentos subidos por API). Si la carpeta/workspace no existe todavía, se
crea automáticamente en la primera llamada.

### `GET /api/documents/carpetas`
Lista los casos disponibles (las subcarpetas dentro de `Documentos/`), para
que el frontend arme la ruta sin tener que conocerlas de antemano.

```json
// Response
["municipalidad-san-juan", "municipalidad-el-progreso"]
```

### `POST /api/chat/{carpeta}`
Envía un mensaje al caso indicado y responde usando el contexto de sus
documentos (embebidos + fijados).

```json
// Request → POST /api/chat/municipalidad-san-juan
{ "message": "Según el marco legal y los formularios ingresados, ¿la municipalidad está preparada para una asociación público-privada?", "mode": "chat" }

// Response
{
  "answer": "Sí, cumple los requisitos... (justificación citando ambos tipos de documento)",
  "sources": [ { "title": "ley-municipal.pdf", "excerpt": "..." } ]
}
```

`mode` puede ser:
- `"query"`: solo responde si encuentra contexto relevante en los documentos.
- `"chat"`: responde también con conocimiento general del modelo si no hay
  contexto — recomendado cuando usas documentos fijados ("pin"), ya que estos
  siempre están presentes independientemente del modo.

### `GET /api/documents/{carpeta}`
Lista los documentos de ese caso (de su carpeta fija, subidos por API, y
generados), indicando si ya están embebidos (disponibles para preguntas) en
su workspace de AnythingLLM.

### `POST /api/documents/{carpeta}/sync`
Vuelve a escanear `Documentos/{carpeta}` y embebe + fija ("pin") los archivos
nuevos que encuentre en el workspace de ese caso. Se ejecuta automáticamente
al iniciar el backend para cada carpeta existente; llama a este endpoint si
agregaste archivos después sin reiniciar.

```json
// Response
{
  "newlyEmbedded": ["ley-municipal.pdf", "formulario-2024.pdf"],
  "alreadyEmbedded": [],
  "errors": []
}
```

### `POST /api/documents/{carpeta}/upload` (multipart/form-data, campo `file`)
Guarda el archivo en el servidor (`Storage/Uploads/{carpeta}`) y lo sube +
embebe en el workspace de ese caso para poder preguntarle sobre su contenido.

Extensiones soportadas: `.pdf .txt .md .docx .csv .json`.

### `POST /api/documents/{carpeta}/generate`
Pide al modelo que redacte un documento nuevo para ese caso.

```json
// Request → POST /api/documents/municipalidad-san-juan/generate
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
vuelve a subir y embeber en el workspace de ese caso, así el chat puede
responder preguntas sobre lo que acaba de generar.

### `GET /api/documents/{carpeta}/download/{fileName}`
Descarga un documento (subido o generado) de ese caso.

## 7. Consumo desde Angular

Ejemplo mínimo de servicio Angular (`chat.service.ts`), parametrizado por caso:

```typescript
@Injectable({ providedIn: 'root' })
export class ChatService {
  private readonly baseUrl = 'http://localhost:5000/api';

  constructor(private http: HttpClient) {}

  listCarpetas() {
    return this.http.get<string[]>(`${this.baseUrl}/documents/carpetas`);
  }

  chat(carpeta: string, message: string, mode: 'query' | 'chat' = 'chat') {
    return this.http.post<{ answer: string; sources: { title: string; excerpt: string }[] }>(
      `${this.baseUrl}/chat/${carpeta}`, { message, mode }
    );
  }

  listDocuments(carpeta: string) {
    return this.http.get<any[]>(`${this.baseUrl}/documents/${carpeta}`);
  }

  uploadDocument(carpeta: string, file: File) {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post(`${this.baseUrl}/documents/${carpeta}/upload`, formData);
  }

  generateDocument(carpeta: string, prompt: string, fileName?: string) {
    return this.http.post(`${this.baseUrl}/documents/${carpeta}/generate`, { prompt, fileName });
  }

  syncCarpeta(carpeta: string) {
    return this.http.post(`${this.baseUrl}/documents/${carpeta}/sync`, {});
  }

  downloadUrl(carpeta: string, fileName: string) {
    return `${this.baseUrl}/documents/${carpeta}/download/${encodeURIComponent(fileName)}`;
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
    │   ├── ChatController.cs       # POST /api/chat/{carpeta}
    │   └── DocumentsController.cs  # GET/POST /api/documents/{carpeta}/...
    ├── Services/
    │   ├── AnythingLlmClient.cs      # Cliente HTTP hacia la API de AnythingLLM (workspaces, chat, docs)
    │   ├── DocumentStorageService.cs # Documentos subidos/generados por API, por carpeta
    │   ├── DocumentSyncService.cs    # Escanea Documentos/{carpeta} y embebe + fija (pin) en su workspace
    │   └── SlugHelper.cs             # Sanitiza el nombre de carpeta (evita path traversal)
    ├── Models/                       # DTOs públicos + contratos internos de AnythingLLM
    ├── Options/
    │   ├── AnythingLlmOptions.cs
    │   └── DocumentSyncOptions.cs
    ├── Documentos/                   # Un subdirectorio por caso: coloca aquí marco legal, formularios, etc.
    ├── Storage/                      # Uploads/{carpeta} y Generated/{carpeta} (runtime, no versionado)
    └── Program.cs
```

## Notas

- Las rutas exactas de la Developer API de AnythingLLM (workspaces, `update-pin`,
  detalle de workspace) pueden variar entre versiones; verifícalas en
  `http://localhost:3001/api/docs` (Swagger propio de AnythingLLM) si
  actualizas la imagen de Docker. Si tu versión no soporta `update-pin` vía
  API, puedes fijar cada documento manualmente desde la interfaz de
  AnythingLLM (ícono de pin junto al documento, dentro del workspace de ese
  caso) — el backend seguirá funcionando igual para embeber/buscar,
  simplemente no automatiza ese último paso.
- Este backend no implementa autenticación de usuarios (fuera del alcance del
  ejemplo); en un entorno real conviene añadir autenticación/autorización
  antes de exponerlo públicamente, sobre todo porque cualquiera que conozca
  el nombre de una carpeta puede consultarla.
