using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Raven.Client.Documents;
using SupportDesk.Api.Endpoints;
using SupportDesk.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddRavenDBClient(connectionName: "ravendb");

// Configure JSON to handle string enums
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Auth:Authority"];
        options.Audience = builder.Configuration["Auth:Audience"];
        options.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = true };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Ensure database exists and indexes are deployed
var store = app.Services.GetRequiredService<IDocumentStore>();
await store.EnsureDatabaseExistsAsync();
store.DeployIndexes(typeof(Program).Assembly);

app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapTicketEndpoints();

app.MapGet("/", () => Results.Ok(new { name = "SupportDesk API is running.", status = "ok" }));

app.Run();
