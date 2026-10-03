using System.Text.RegularExpressions;

// ============================================================
// Taller: API de Matrícula Académica - Universidad del Sinú
// Minimal API .NET que modela la relación N:M (Estudiante <-> Asignatura)
// mediante la entidad intermedia Matrícula.
// ============================================================

var builder = WebApplication.CreateBuilder(args);

// Render inyecta el puerto por la variable de entorno PORT.
// Sin esto, la app no encontraria el puerto que el servicio le asigno.
var puerto = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(puerto))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{puerto}");
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// ============================================================
// COLECCIONES DE DATOS INICIALES (simulan la base de datos)
// ============================================================

var estudiantes = new List<Estudiante>
{
    new(1, "Ana Torres", "Ingeniería de Sistemas"),
    new(2, "Carlos Ramírez", "Ingeniería de Sistemas"),
    new(3, "María López", "Contaduría")
};

var asignaturas = new List<Asignatura>
{
    new Asignatura(101, "Estructuras de Datos", "SIS-101", 3),
    new Asignatura(102, "Programación Web", "SIS-102", 4),
    new Asignatura(103, "Bases de Datos I", "SIS-103", 3)
};

var matriculas = new List<Matricula>();

// Formato de periodo lectivo esperado: 2026-1 o 2026-2
var formatoPeriodo = new Regex(@"^\d{4}-[12]$", RegexOptions.Compiled);

// ============================================================
// RUTA RAÍZ
// ============================================================

app.MapGet("/", () => Results.Ok(new
{
    taller = "API de Matrícula Académica - Universidad del Sinú",
    mensaje = "Use /swagger para ver y probar todos los endpoints.",
    estudiantes = estudiantes.Count,
    asignaturasActivas = asignaturas.Count(a => a.Activa),
    matriculasRegistradas = matriculas.Count
}))
.WithName("Raiz")
.WithSummary("Información general de la API")
.Produces(StatusCodes.Status200OK);

// ============================================================
// REGLA 3 - OFERTA ACADÉMICA DISPONIBLE
// GET /api/asignaturas -> solo las activas
// ============================================================

app.MapGet("/api/asignaturas", () =>
{
    // REGLA 3: solo se ofertan las asignaturas con Activa == true.
    var activas = asignaturas.Where(a => a.Activa).ToList();
    return Results.Ok(activas);
})
.WithName("ListarAsignaturasOfertadas")
.WithSummary("Consulta las asignaturas ofertadas (activas)")
.WithDescription("REGLA 3: devuelve únicamente las asignaturas con Activa == true. " +
                  "Las asignaturas inactivadas por borrado lógico quedan ocultas de la oferta académica.")
.Produces<List<Asignatura>>(StatusCodes.Status200OK);

// ============================================================
// GET /api/asignaturas/{id} -> consulta una asignatura (incluye inactivas)
// ============================================================

app.MapGet("/api/asignaturas/{id}", (int id) =>
{
    var asignatura = asignaturas.FirstOrDefault(a => a.Id == id);
    if (asignatura is null)
    {
        return Results.NotFound(new { error = $"No existe la asignatura con Id {id}." });
    }

    return Results.Ok(asignatura);
})
.WithName("ObtenerAsignatura")
.WithSummary("Consulta una asignatura por su Id")
.WithDescription("A diferencia de GET /api/asignaturas, este endpoint sí devuelve las " +
                  "asignaturas inactivas, permitiendo verificar el borrado lógico (Activa == false).")
.Produces<Asignatura>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

// ============================================================
// POST /api/asignaturas -> registrar nueva asignatura
// ============================================================

app.MapPost("/api/asignaturas", (NuevaAsignatura nueva) =>
{
    if (string.IsNullOrWhiteSpace(nueva.Nombre))
    {
        return Results.BadRequest(new { error = "El campo 'Nombre' es obligatorio y no puede estar vacío." });
    }

    if (string.IsNullOrWhiteSpace(nueva.Codigo))
    {
        return Results.BadRequest(new { error = "El campo 'Codigo' es obligatorio y no puede estar vacío." });
    }

    if (nueva.Creditos <= 0)
    {
        return Results.BadRequest(new { error = "El campo 'Creditos' debe ser mayor a 0." });
    }

    // El código es único en el catálogo de asignaturas.
    if (asignaturas.Any(a => string.Equals(a.Codigo, nueva.Codigo.Trim(), StringComparison.OrdinalIgnoreCase)))
    {
        return Results.Conflict(new { error = $"Ya existe una asignatura con el código '{nueva.Codigo.Trim()}'." });
    }

    var nuevoId = asignaturas.Count > 0 ? asignaturas.Max(a => a.Id) + 1 : 101;
    var asignatura = new Asignatura(nuevoId, nueva.Nombre.Trim(), nueva.Codigo.Trim(), nueva.Creditos);

    asignaturas.Add(asignatura);

    return Results.Created($"/api/asignaturas/{asignatura.Id}", asignatura);
})
.WithName("CrearAsignatura")
.WithSummary("Registra una nueva asignatura académica")
.WithDescription("El Id se genera automáticamente en el servidor. Toda asignatura creada " +
                  "inicia con Activa == true, por lo que queda incluida en la oferta académica.")
.Produces<Asignatura>(StatusCodes.Status201Created)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status409Conflict);

// ============================================================
// REGLA 1 - BORRADO LÓGICO / FÍSICO
// DELETE /api/asignaturas/{id}
// ============================================================

app.MapDelete("/api/asignaturas/{id}", (int id) =>
{
    var indice = asignaturas.FindIndex(a => a.Id == id);
    if (indice < 0)
    {
        return Results.NotFound(new { error = $"No existe la asignatura con Id {id}." });
    }

    var asignatura = asignaturas[indice];

    // Historial académico: matrículas registradas en cualquier año y periodo.
    var matriculasAsociadas = matriculas
        .Where(m => m.AsignaturaId == id)
        .ToList();

    // --- Rama 1: sin matrículas -> BORRADO FÍSICO ---
    if (matriculasAsociadas.Count == 0)
    {
        asignaturas.RemoveAt(indice);
        return Results.NoContent();
    }

    // Una asignatura ya inactiva y con historial no se procesa nuevamente.
    if (!asignatura.Activa)
    {
        return Results.BadRequest(new
        {
            error = $"La asignatura '{asignatura.Nombre}' ya se encuentra inactiva y su historial no puede alterarse."
        });
    }

    // --- Rama 2: con matrículas -> BORRADO LÓGICO ---
    // Los records son inmutables: se reconstruye con 'with' y se reemplaza en la lista.
    asignaturas[indice] = asignatura with { Activa = false };

    var estudiantesAfectados = matriculasAsociadas
        .Select(m => estudiantes.FirstOrDefault(e => e.Id == m.EstudianteId)?.Nombre)
        .Where(nombre => nombre is not null)
        .Distinct()
        .ToList();

    return Results.Ok(new
    {
        mensaje = $"La asignatura '{asignatura.Nombre}' se inactivó lógicamente porque posee matricículas asociadas.",
        tipoEliminacion = "BORRADO LÓGICO",
        asignaturaInactivada = asignaturas[indice],
        matriculasPreservadas = matriculasAsociadas.Count,
        estudiantesConHistorialPreservado = estudiantesAfectados
    });
})
.WithName("EliminarAsignatura")
.WithSummary("Elimina la asignatura: física si no tiene matrículas, lógica si las tiene")
.WithDescription("REGLA 1 - Borrado lógico:\n\n" +
                  "• Si la asignatura NO tiene matrículas asociadas en ningún periodo, se elimina " +
                  "FÍSICAMENTE de la lista y responde 204 No Content.\n\n" +
                  "• Si la asignatura YA tiene estudiantes matriculados, NO se borra: se aplica " +
                  "borrado lógico cambiando Activa a false, preservando el historial académico, " +
                  "y responde 200 OK con el detalle de la operación.\n\n" +
                  "• Si la Id no existe, responde 404 Not Found.")
.Produces(StatusCodes.Status204NoContent)
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status404NotFound);

// ============================================================
// REGLA 2 - VALIDACIONES DEL PROCESO DE MATRÍCULA
// POST /api/matriculas
// ============================================================

app.MapPost("/api/matriculas", (NuevaMatricula nueva) =>
{
    // --- Validación del Año y Periodo lectivo ---
    if (nueva.Anio < 2000 || nueva.Anio > 2100)
    {
        return Results.BadRequest(new { error = $"El año '{nueva.Anio}' no es válido. Debe estar entre 2000 y 2100." });
    }

    if (string.IsNullOrWhiteSpace(nueva.Periodo))
    {
        return Results.BadRequest(new { error = "El campo 'Periodo' es obligatorio y no puede estar vacío." });
    }

    if (!formatoPeriodo.IsMatch(nueva.Periodo))
    {
        return Results.BadRequest(new
        {
            error = $"El periodo '{nueva.Periodo}' no tiene un formato válido. Se espera el formato AÑO-1 o AÑO-2 (ejemplo: 2026-1)."
        });
    }

    if (!nueva.Periodo.StartsWith(nueva.Anio.ToString(), StringComparison.Ordinal))
    {
        return Results.BadRequest(new
        {
            error = $"El periodo '{nueva.Periodo}' no corresponde al año académico '{nueva.Anio}'."
        });
    }

    // --- El estudiante debe existir en el sistema ---
    var estudiante = estudiantes.FirstOrDefault(e => e.Id == nueva.EstudianteId);
    if (estudiante is null)
    {
        return Results.NotFound(new { error = $"No existe el estudiante con Id {nueva.EstudianteId}." });
    }

    // --- La asignatura debe existir ---
    var asignatura = asignaturas.FirstOrDefault(a => a.Id == nueva.AsignaturaId);
    if (asignatura is null)
    {
        return Results.NotFound(new { error = $"No existe la asignatura con Id {nueva.AsignaturaId}." });
    }

    // --- No se permite matricular en asignaturas inactivas (400 Bad Request) ---
    if (!asignatura.Activa)
    {
        return Results.BadRequest(new
        {
            error = $"No es posible matricular en la asignatura '{asignatura.Nombre}' porque se encuentra inactiva."
        });
    }

    // --- Control de duplicados dentro del mismo año y periodo ---
    var duplicada = matriculas.Any(m =>
        m.EstudianteId == nueva.EstudianteId &&
        m.AsignaturaId == nueva.AsignaturaId &&
        m.Anio == nueva.Anio &&
        m.Periodo == nueva.Periodo);

    if (duplicada)
    {
        return Results.Conflict(new
        {
            error = $"El estudiante '{estudiante.Nombre}' ya está matriculado en '{asignatura.Nombre}' para el periodo {nueva.Anio}-{nueva.Periodo.Split('-')[1]}."
        });
    }

    var nuevoId = matriculas.Count > 0 ? matriculas.Max(m => m.Id) + 1 : 1;
    var matricula = new Matricula(nuevoId, nueva.EstudianteId, nueva.AsignaturaId, nueva.Anio, nueva.Periodo);

    matriculas.Add(matricula);

    return Results.Created($"/api/matriculas/{matricula.Id}", matricula);
})
.WithName("CrearMatricula")
.WithSummary("Matricular un estudiante en una asignatura")
.WithDescription("REGLA 2 - Validaciones:\n\n" +
                  "• El estudiante debe existir en el sistema (404 si no existe).\n\n" +
                  "• La asignatura debe existir (404 si no existe).\n\n" +
                  "• La asignatura debe estar Activa == true; si está inactiva responde 400 Bad Request.\n\n" +
                  "• No se permite duplicar la misma asignatura para el mismo año y periodo " +
                  "(409 Conflict).\n\n" +
                  "• El año y el periodo deben ser coherentes y tener el formato AÑO-1 o AÑO-2 (400 si no).")
.Produces<Matricula>(StatusCodes.Status201Created)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status409Conflict);

// ============================================================
// GET /api/estudiantes/{id}/asignaturas -> historial académico
// ============================================================

app.MapGet("/api/estudiantes/{id}/asignaturas", (int id) =>
{
    var estudiante = estudiantes.FirstOrDefault(e => e.Id == id);
    if (estudiante is null)
    {
        return Results.NotFound(new { error = $"No existe el estudiante con Id {id}." });
    }

    // El historial incluye asignaturas inactivas: ese es el propósito del borrado lógico.
    var historial = matriculas
        .Where(m => m.EstudianteId == id)
        .OrderBy(m => m.Anio)
        .ThenBy(m => m.Periodo)
        .ThenBy(m => m.Id)
        .Select(m =>
        {
            var asignatura = asignaturas.FirstOrDefault(a => a.Id == m.AsignaturaId);

            return new MatriculaDetalle(
                m.Id,
                asignatura?.Nombre ?? "(asignatura eliminada)",
                asignatura?.Codigo ?? "SIN-CODIGO",
                asignatura?.Creditos ?? 0,
                asignatura?.Activa ?? false,
                m.Anio,
                m.Periodo
            );
        })
        .ToList();

    return Results.Ok(new
    {
        estudiante = new { estudiante.Id, estudiante.Nombre, estudiante.Carrera },
        totalAsignaturasMatriculadas = historial.Count,
        totalCreditos = historial.Sum(h => h.Creditos),
        asignaturas = historial
    });
})
.WithName("ConsultarHistorialEstudiante")
.WithSummary("Consulta el historial de asignaturas matriculadas por un estudiante")
.WithDescription("Devuelve el historial académico con año y periodo de cada matrícula. " +
                  "Conserva las asignaturas inactivas por borrado lógico, de modo que el " +
                  "historial no se pierde aunque la asignatura deje de ofertarse.")
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

// ============================================================
// ENDPOINTS DE APOYO (facilitan la prueba en Swagger)
// ============================================================

app.MapGet("/api/estudiantes", () => Results.Ok(estudiantes))
    .WithName("ListarEstudiantes")
    .WithSummary("Lista los estudiantes matriculables")
    .WithDescription("Endpoint de apoyo: permite consultar los Id de estudiante para probar POST /api/matriculas.")
    .Produces<List<Estudiante>>(StatusCodes.Status200OK);

app.MapGet("/api/estudiantes/{id}", (int id) =>
{
    var estudiante = estudiantes.FirstOrDefault(e => e.Id == id);
    return estudiante is null
        ? Results.NotFound(new { error = $"No existe el estudiante con Id {id}." })
        : Results.Ok(estudiante);
})
.WithName("ObtenerEstudiante")
.WithSummary("Consulta un estudiante por su Id")
.Produces<Estudiante>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

app.MapGet("/api/matriculas", () => Results.Ok(matriculas))
    .WithName("ListarMatriculas")
    .WithSummary("Lista todas las matrículas registradas")
    .WithDescription("Endpoint de apoyo: permite verificar el estado real de la entidad intermedia " +
                      "que modela la relación N:M entre estudiantes y asignaturas.")
    .Produces<List<Matricula>>(StatusCodes.Status200OK);

app.Run();

// ============================================================
// MODELO DE DATOS (Records)
// ============================================================

// 1. Modelo de Estudiante
record Estudiante(int Id, string Nombre, string Carrera);

// 2. Modelo de Asignatura (Incluye estado para Borrado Lógico)
record Asignatura(int Id, string Nombre, string Codigo, int Creditos, bool Activa = true);

// 3. Modelo de Matrícula (Entidad Intermedia con Año y Periodo Académico)
record Matricula(int Id, int EstudianteId, int AsignaturaId, int Anio = 2026, string Periodo = "2026-1");

// DTO de entrada para crear asignaturas: el Id y el estado los asigna el servidor.
record NuevaAsignatura(string Nombre, string Codigo, int Creditos);

// DTO de entrada para matrícula: conserva los valores por defecto de la guía (2026-1).
record NuevaMatricula(int EstudianteId, int AsignaturaId, int Anio = 2026, string Periodo = "2026-1");

// Proyección del historial: combina la matrícula con los datos de la asignatura.
record MatriculaDetalle(
    int Id,
    string Nombre,
    string Codigo,
    int Creditos,
    bool Activa,
    int Anio,
    string Periodo
);