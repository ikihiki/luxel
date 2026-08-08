using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Luxel.Gallery;

/// <summary>Generic Host/DIへStory catalog providerを登録する内部契約。</summary>
public interface IStoryCatalogRegistration
{
    void Register(StoryCatalogBuilder builder);
}

/// <summary>Story projectをGeneric Host形式で合成するための共通登録API。</summary>
public static class StoryServiceCollectionExtensions
{
    public static IServiceCollection AddStoryCatalog(
        this IServiceCollection services,
        Action<StoryCatalogBuilder> registration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registration);
        services.AddSingleton<IStoryCatalogRegistration>(new DelegateStoryCatalogRegistration(registration));
        services.TryAddSingleton(static provider =>
        {
            var builder = new StoryCatalogBuilder();
            foreach (IStoryCatalogRegistration item in provider.GetServices<IStoryCatalogRegistration>())
                item.Register(builder);
            return builder.Build();
        });
        return services;
    }

    private sealed class DelegateStoryCatalogRegistration(Action<StoryCatalogBuilder> registration)
        : IStoryCatalogRegistration
    {
        public void Register(StoryCatalogBuilder builder) => registration(builder);
    }
}
