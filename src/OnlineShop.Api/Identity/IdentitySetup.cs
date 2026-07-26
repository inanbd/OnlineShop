using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using OnlineShop.Application.Abstractions.Identity;
using OnlineShop.Persistence;

namespace OnlineShop.Api.Identity;

public static class IdentitySetup
{
    /// <summary>Authorization policy for the merchant back-office.</summary>
    public const string MerchantPolicy = "MerchantOnly";

    /// <summary>
    /// Wires ASP.NET Core Identity onto the Dapper-backed stores.
    /// </summary>
    /// <remarks>
    /// Note the absence of <c>AddEntityFrameworkStores</c>: rule 1 forbids
    /// Entity Framework, so <see cref="Persistence.DependencyInjection.AddDapperIdentityStores"/>
    /// supplies <c>IUserStore</c> and <c>IRoleStore</c> instead.
    /// </remarks>
    public static IServiceCollection AddOnlineShopIdentity(this IServiceCollection services)
    {
        services
            .AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

                // No mail is sent from this application, so requiring
                // confirmation would lock every new account out of its own shop.
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddRoles<AppRole>()
            .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddDapperIdentityStores();

        services
            .AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme, options =>
            {
                options.LoginPath = "/Account/Login";
                options.LogoutPath = "/Account/Logout";
                options.AccessDeniedPath = "/Account/AccessDenied";
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(MerchantPolicy, policy => policy.RequireRole(AppRoles.Merchant));

        return services;
    }
}
