﻿#region Licence
/* The MIT License (MIT)
Copyright © 2014 Ian Cooper <ian_hammond_cooper@yahoo.co.uk>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the “Software”), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE. */

#endregion

using System;
using FluentAssertions;
using Paramore.Brighter.Core.Tests.CommandProcessors.TestDoubles;
using Paramore.Brighter.Core.Tests.FeatureSwitch.TestDoubles;
using Polly.Registry;
using Microsoft.Extensions.DependencyInjection;
using Paramore.Brighter.Extensions.DependencyInjection;
using Xunit;
using Paramore.Brighter.FeatureSwitch.Handlers;

namespace Paramore.Brighter.Core.Tests.FeatureSwitch
{
    [Collection("CommandProcessor")]
    public class CommandProcessorWithFeatureSwitchOffInPipelineTests : IDisposable
    {
        private readonly MyCommand _myCommand = new MyCommand();
        private readonly SubscriberRegistry _registry;
        private readonly ServiceProviderHandlerFactory _handlerFactory;

        private CommandProcessor _commandProcessor;

        public CommandProcessorWithFeatureSwitchOffInPipelineTests()
        {            
            _registry = new SubscriberRegistry();
            _registry.Register<MyCommand, MyFeatureSwitchedOffHandler>();

            var container = new ServiceCollection();
            container.AddSingleton<MyFeatureSwitchedOffHandler>();
            container.AddTransient<FeatureSwitchHandler<MyCommand>>();
            container.AddSingleton<IBrighterOptions>(new BrighterOptions() {HandlerLifetime = ServiceLifetime.Transient});

            _handlerFactory = new ServiceProviderHandlerFactory(container.BuildServiceProvider());
        }

        [Fact]
        public void When_Sending_A_Command_To_The_Processor_When_A_Feature_Switch_Is_Off()
        {
            _commandProcessor = CommandProcessorBuilder
                .With()
                .Handlers(new HandlerConfiguration(_registry, _handlerFactory))
                .DefaultPolicy()
                .NoExternalBus()
                .RequestContextFactory(new InMemoryRequestContextFactory())
                .Build();
            
            _commandProcessor.Send(_myCommand);

            MyFeatureSwitchedOffHandler.DidReceive(_myCommand).Should().BeFalse();
        }

        public void Dispose()
        {
            CommandProcessor.ClearExtServiceBus();
        }
    }
}
