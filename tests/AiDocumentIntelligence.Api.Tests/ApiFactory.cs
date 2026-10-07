using AiDocumentIntelligence.Domain;
using AiDocumentIntelligence.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;

namespace AiDocumentIntelligence.Api.Tests;

// Boots the real API pipeline (routing, authentication, authorization, controllers) with the data and AI
// dependencies mocked, so no Postgres, Azurite or Ollama is needed.
public class ApiFactory : WebApplicationFactory<Program>
{
    // By default Entra token validation is replaced with the header-driven test scheme.
    protected virtual void ConfigureAuthentication(IServiceCollection services)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
            options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            options.DefaultForbidScheme = TestAuthHandler.SchemeName;
        }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            ConfigureAuthentication(services);

            // The background worker would otherwise resolve real services.
            services.RemoveAll<IHostedService>();

            var claims = new Mock<IClaimRepository>();
            claims.Setup(r => r.GetClaimsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<ClaimStatus?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            services.Replace(ServiceDescriptor.Scoped(_ => claims.Object));

            var documents = new Mock<IDocumentRepository>();
            documents.Setup(r => r.GetDocumentsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            services.Replace(ServiceDescriptor.Scoped(_ => documents.Object));

            services.Replace(ServiceDescriptor.Scoped(_ => Mock.Of<IDocumentStorage>()));
            services.Replace(ServiceDescriptor.Singleton(Mock.Of<IDocumentProcessingQueue>()));
            services.Replace(ServiceDescriptor.Scoped(_ => Mock.Of<IDocumentQuestionAnswerer>()));
        });
    }
}
