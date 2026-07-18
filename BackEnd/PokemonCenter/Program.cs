using Microsoft.EntityFrameworkCore;
using PokemonCenter.Endpoints;
using PokemonCenter.Exceptions;
using PokemonCenter.Models;
using PokemonCenter.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PokemonCenterContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("PokemonCenter")));

builder.Services.AddScoped<TreinadorService>();
builder.Services.AddScoped<PokemonService>();
builder.Services.AddScoped<MatriculaService>();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUi(options =>
    {
        options.DocumentPath = "/openapi/v1.json";
    });
}

app.UseExceptionHandler();

app.MapTreinadorEndpoints();
app.MapPokemonEndpoints();
app.MapMatriculaEndpoints();

app.Run();
