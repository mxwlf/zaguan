var builder = DistributedApplication.CreateBuilder(args);

var keyCloakAdminUsername = builder.AddParameter("keyCloakAdminUsername");
var keyCloakAdminPassword = builder.AddParameter("keyCloakAdminPassword", secret: true);

builder.AddKeycloak("keycloak", 8080, keyCloakAdminUsername, keyCloakAdminPassword);

await builder.Build().RunAsync().ConfigureAwait(false);
