using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using WheelWizard.Views.Navigation;

namespace WheelWizard.Test.Views;

public class NavigationTests
{
    [Fact]
    public void Factory_CombinesRegisteredDependenciesWithNavigationArguments()
    {
        var dependency = new PageDependency();
        using var services = new ServiceCollection().AddSingleton(dependency).BuildServiceProvider();
        var factory = new PageFactory(services);

        var page = factory.Create<DetailsPage>(42);

        Assert.Same(dependency, page.Dependency);
        Assert.Equal(42, page.ItemId);
        Assert.NotSame(page, factory.Create<DetailsPage>(42));
        Assert.Throws<ArgumentException>(() => factory.Create(typeof(string)));
    }

    [Fact]
    public void Navigation_PublishesThePageAndPreservesItWhenCreationFails()
    {
        var factory = Substitute.For<IPageFactory>();
        var page = new UserControl();
        factory.Create(typeof(UserControl), Arg.Any<object[]>()).Returns(page);
        factory.Create(typeof(DetailsPage), Arg.Any<object[]>()).Returns(_ => throw new InvalidOperationException("Missing dependency"));
        var navigation = new NavigationService(factory);
        var received = new List<UserControl>();
        navigation.PageChanged += (_, current) =>
        {
            Assert.Same(current, navigation.CurrentPage);
            received.Add(current);
        };

        navigation.NavigateTo<UserControl>();
        Assert.Throws<InvalidOperationException>(() => navigation.NavigateTo<DetailsPage>());

        Assert.Same(page, navigation.CurrentPage);
        Assert.Equal([page], received);
    }

    [Fact]
    public void Navigation_RedirectDuringConstructionWinsOverTheOriginalPage()
    {
        var factory = Substitute.For<IPageFactory>();
        var navigation = new NavigationService(factory);
        var destination = new UserControl();
        factory.Create(typeof(UserControl), Arg.Any<object[]>()).Returns(destination);
        factory
            .Create(typeof(DetailsPage), Arg.Any<object[]>())
            .Returns(_ =>
            {
                navigation.NavigateTo<UserControl>();
                return new UserControl();
            });
        var received = new List<UserControl>();
        navigation.PageChanged += (_, page) => received.Add(page);

        navigation.NavigateTo<DetailsPage>();

        Assert.Same(destination, navigation.CurrentPage);
        Assert.Equal([destination], received);
    }

    public sealed class PageDependency;

    public sealed class DetailsPage(PageDependency dependency, int itemId) : UserControl
    {
        public PageDependency Dependency { get; } = dependency;
        public int ItemId { get; } = itemId;
    }
}
