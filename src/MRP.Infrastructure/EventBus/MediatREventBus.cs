using MediatR;
using MRP.Domain.Interfaces;

namespace MRP.Infrastructure.EventBus;

public class MediatREventBus : IEventBus
{
    private readonly IMediator _mediator;

    public MediatREventBus(IMediator mediator) => _mediator = mediator;

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct)
        where TEvent : class
    {
        await _mediator.Publish(new EventNotification<TEvent>(@event), ct);
    }
}

public class EventNotification<T> : INotification where T : class
{
    public T Event { get; }
    public EventNotification(T @event) => Event = @event;
}
