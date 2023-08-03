using MediatR;
using SimpleInjector;
using System.Diagnostics;

namespace BlazorSimpleInjectorMediator
{
    public interface IScoped { public Guid Id { get; init; } }
    public class TestScoped : IScoped
    {
        public TestScoped() => Id = Guid.NewGuid();
        public Guid Id { get; init; }
    }

    public interface IScopedCaller { void CallScoped(); }

    public class ScopedCaller : IScopedCaller
    {
        private readonly IMediator _mediator;
        private readonly IScoped _scoped;
        private readonly Container _container;

        public ScopedCaller(IMediator mediator, IScoped scoped, Container container)
        {
            _mediator = mediator; _scoped = scoped;
            _container = container;
        }
        public void CallScoped()
        {
            var scope = ScopedLifestyle.Scoped.GetCurrentScope(_container);
            Debug.WriteLine(_scoped.Id);
            _mediator.Send(new TestScopedRequest());
        }
    }

    public class TestScopedRequest : IRequest { }

    public class TestScopedRequestHandler : IRequestHandler<TestScopedRequest>
    {
        private readonly IScoped _scoped;
        private readonly Container _container;

        public TestScopedRequestHandler(IScoped scoped, Container container)
        {
            _scoped = scoped;
            _container = container;
        }

        public async Task Handle(TestScopedRequest request, CancellationToken cancellationToken)
        {
            var scope = ScopedLifestyle.Scoped.GetCurrentScope(_container);
            Debug.WriteLine(_scoped.Id);
            Debug.WriteLine(_container.GetInstance<IScoped>().Id);            
        }
    }
}
