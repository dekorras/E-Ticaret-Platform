using System.Text;
using System.Threading.RateLimiting;
using Dekorras.Api.Auth;
using Dekorras.Api.Jobs;
using Dekorras.Api.Middleware;
using Dekorras.Application;
using Dekorras.Infrastructure;
using Dekorras.Infrastructure.Web;
using Dekorras.Persistence;
using Hangfire;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Bkz. plan §11 - "rate limiting". Mobil/üçüncü taraf istemcilerin kullandığı JWT
// register/login/refresh uç noktaları IP başına brute-force denemesine karşı sınırlanır.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.AddApplication();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Dekorras API", Version = "v1" });

    var jwtScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Bearer token girin: {token}",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };
    options.AddSecurityDefinition("Bearer", jwtScheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [jwtScheme] = [] });
});

var jwtSigningKey = builder.Configuration["Jwt:SigningKey"]!;
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddScoped<JwtAccessTokenGenerator>();

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// Zamanlanmış arka plan işleri (bkz. plan §7 - "Hangfire ile periyodik güncellenir") YALNIZCA burada,
// Api projesinde çalışır - Admin/Storefront yalnızca sonucu (ExchangeRate tablosu) okur, kendi
// Hangfire sunucularını çalıştırmazlar (aksi halde aynı işi üç ayrı süreç zamanlamaya çalışırdı).
builder.Services.AddHangfire(config => config.UseSqlServerStorage(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddHangfireServer();
builder.Services.AddScoped<ExchangeRateRefreshJob>();

var app = builder.Build();

app.UseDekorrasSecurityHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Geliştirme ortamında migration'ları otomatik uygula - production'da CI/CD pipeline'ı yapmalı.
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.MigrateAsync();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    await DbInitializer.SeedAsync(dbContext, userManager);
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseHttpsRedirection();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Hangfire'ın YENİ servis-tabanlı API'si kullanılıyor (`IRecurringJobManager`) - statik
// `RecurringJob.AddOrUpdate` `JobStorage.Current`'ın önceden GlobalConfiguration ile ayarlanmış
// olmasını gerektirir, `AddHangfire(...)` DI kaydı bunu YAPMAZ; canlı smoke test sırasında
// "Current JobStorage instance has not been initialized yet" hatasıyla bulundu.
using (var scope = app.Services.CreateScope())
{
    var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    recurringJobManager.AddOrUpdate<ExchangeRateRefreshJob>("refresh-exchange-rates", job => job.RunAsync(), Cron.Daily());
}

app.Run();
