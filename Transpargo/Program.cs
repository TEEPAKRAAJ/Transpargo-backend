using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Security.Claims;
using System.Text;
using Transpargo.Services;
using Transpargo.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// -------------------------------------------------------
// Controllers + JSON
// -------------------------------------------------------
builder.Services.AddControllers()
    .AddJsonOptions(opt =>
    {
        opt.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    })
    .AddNewtonsoftJson();

// -------------------------------------------------------
// Swagger + JWT Security
// -------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Transpargo API",
        Version = "v1"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Enter: Bearer {your JWT token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// -------------------------------------------------------
// CORS (React)
// -------------------------------------------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// -------------------------------------------------------
// JWT Authentication
// -------------------------------------------------------
var jwtKey = builder.Configuration["Jwt:Key"];
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,

            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)
            ),
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var identity = context.Principal?.Identity as ClaimsIdentity;
                var role = identity?.FindFirst("role")?.Value;

                if (!string.IsNullOrEmpty(role))
                    identity!.AddClaim(new Claim(ClaimTypes.Role, role));

                return Task.CompletedTask;
            }
        };
    });

// -------------------------------------------------------
// Authorization
// -------------------------------------------------------
builder.Services.AddAuthorization();

// -------------------------------------------------------
// App Services
// -------------------------------------------------------
builder.Services.AddHttpClient();
builder.Services.AddSingleton<SupabaseServices>();
builder.Services.AddSingleton<ShipmentService>();
builder.Services.AddSingleton<IHsCodeService, HsCodeService>();
builder.Services.AddSingleton<AiRiskService>();

builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<TradeComplianceService>();
builder.Services.AddScoped<ShippingCostService>();
builder.Services.AddHttpClient<INimService, NimService>();

Environment.SetEnvironmentVariable(
    "DEEPSEEK_API_KEY",
    builder.Configuration["DEEPSEEK_API_KEY"]
);

var app = builder.Build();

// -------------------------------------------------------
// Middleware
// -------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Bind to port 5000
app.Urls.Add("http://0.0.0.0:5000");

app.Run();
