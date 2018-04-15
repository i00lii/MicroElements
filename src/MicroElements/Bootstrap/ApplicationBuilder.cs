﻿// Copyright (c) MicroElements. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MicroElements.Bootstrap.Extensions;
using MicroElements.Bootstrap.Extensions.Configuration;
using MicroElements.Bootstrap.Utils;
using MicroElements.Configuration;
using MicroElements.Configuration.Evaluation;
using MicroElements.DependencyInjection;
using MicroElements.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MicroElements.Bootstrap
{
    /// <summary>
    /// Построитель приложения.
    /// </summary>
    public class ApplicationBuilder : IApplicationBuilder
    {
        private BuildContext _buildContext;

        /// <summary>
        /// Составляем и запускаем приложение.
        /// </summary>
        /// <param name="startupConfiguration">Параметры запуска приложения.</param>
        /// <returns>Провайдер к коллекции зарегистрированных сервисов.</returns>
        public IServiceProvider BuildAndStart(StartupConfiguration startupConfiguration)
        {
            var buildContext = Build(startupConfiguration);
            return Start(buildContext);
        }

        /// <summary>
        /// Составляем приложение.
        /// </summary>
        /// <param name="startupConfiguration">Параметры запуска приложения.</param>
        /// <returns>Провайдер к коллекции зарегистрированных сервисов.</returns>
        public IBuildContext Build(StartupConfiguration startupConfiguration)
        {
            _buildContext = new BuildContext { StartupConfiguration = startupConfiguration, };
            var measureSession = new MeasureSession("Application startup");

            // Разбор параметров командной строки
            startupConfiguration.BuildUpFromCommandLineArgs(startupConfiguration.CommandLineArgs.Args);

            // Can use defined service collection or create new
            _buildContext.ServiceCollection = startupConfiguration.ServiceCollection ?? new ServiceCollection();
            var serviceCollection = _buildContext.ServiceCollection;

            using (measureSession.StartTimer("ConfigureLogging"))
            {
                // Установка путей логирования, создание и блокирование pid-файла
                // todo: UseCentralizedLogging
                var unlocker = LoggingExtensions.SetupLogsPath(startupConfiguration);
                serviceCollection.AddSingleton(unlocker);

                // Получение сконфигурированной фабрики логирования.
                var configureLogging = startupConfiguration.ConfigureLogging ?? DefaultLogging.ConfigureLogging;
                _buildContext.LoggerFactory = configureLogging();
                _buildContext.Logger = _buildContext.LoggerFactory.CreateLogger("Bootstrap");
            }

            using (measureSession.StartTimer("LoadTypes"))
            {
                // Получение информации об окружении.
                _buildContext.StartupInfo = ReflectionUtils.GetStartupInfo();

                // Переключим текущую директорию на директорию запуска.
                Directory.SetCurrentDirectory(_buildContext.StartupInfo.BaseDirectory);
                _buildContext.StartupInfo.CurrentDirectory = _buildContext.StartupInfo.BaseDirectory;

                // Загрузка сборок в память
                _buildContext.Assemblies = ReflectionUtils
                    .LoadAssemblies(_buildContext.StartupInfo.BaseDirectory, _buildContext.StartupConfiguration.AssemblyScanPatterns)
                    .Concat(new[] { typeof(ApplicationBuilder).Assembly })
                    .Distinct()
                    .ToArray();

                _buildContext.Logger.LogDebug($"Loaded {_buildContext.Assemblies.Length} assemblies");

                if (_buildContext.Assemblies.Length > 20)
                {
                    var assemblyScanPatterns = _buildContext.StartupConfiguration.AssemblyScanPatterns;
                    var assemblyScanPatternsText = string.Join(",", assemblyScanPatterns);
                    _buildContext.Logger.LogWarning($"Diagnostic: too many assemblies found. Specify AssemblyScanPatterns. Loaded: {_buildContext.Assemblies.Length} assemblies, AssemblyScanPatterns: {assemblyScanPatternsText}");
                }

                // Список типов
                _buildContext.ExportedTypes = _buildContext.Assemblies.SelectMany(assembly => assembly.GetDefinedTypesSafe()).ToArray();

                _buildContext.LogHeader();//todo: assemblies loaded? types loaded
            }

            using (measureSession.StartTimer("LoadConfiguration"))
            {
                // Загрузка конфигурации
                ConfigurationReader.LoadConfiguration(_buildContext);

                // Регистрируем конфигурацию в виде IConfiguration и IConfigurationRoot
                serviceCollection.Replace(ServiceDescriptor.Singleton<IConfiguration>(_buildContext.ConfigurationRoot));
                serviceCollection.Replace(ServiceDescriptor.Singleton<IConfigurationRoot>(_buildContext.ConfigurationRoot));
            }

            using (measureSession.StartTimer("ConfigureServices"))
            {
                try
                {
                    // Конфигурирование сервисов
                    ConfigureServices(_buildContext);

                    // Строим провайдер.
                    _buildContext.ServiceProvider = _buildContext.ServiceCollection.BuildServiceProvider();

                    if (startupConfiguration.ExternalBuilder != null)
                    {
                        _buildContext.ServiceProvider = ConfigureServicesExt(_buildContext);
                    }
                }
                catch (Exception exception)
                {
                    //todo: right logging
                    _buildContext.Logger.LogError(new EventId(0), exception, exception.Message);
                    throw;
                }
            }

            // Dump значений конфигурации в лог
            if (startupConfiguration.DumpConfigurationToLog)
            {
                _buildContext.ConfigurationRoot.DumpConfigurationToLog(_buildContext.LoggerFactory);
            }

            measureSession.LogMeasures(_buildContext.Logger);

            return _buildContext;
        }

        /// <inheritdoc />
        public IApplicationBuilder ConfigureServices(Action<IServiceCollection> configureServices)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Запускаем приложение.
        /// </summary>
        /// <param name="buildContext">Провайдер сервисов.</param>
        /// <returns>Провайдер к коллекции зарегистрированных сервисов.</returns>
        public IBuildContext Start(IBuildContext buildContext)
        {
            _buildContext.Logger.LogInformation("Starting services");
            var measureSession = new MeasureSession("Starting services");
            measureSession.ExecuteWithTimer("StartRunnables", () =>
            {
                // Запуск сервисов.
                buildContext.ServiceProvider.StartRunnablesAsync(_buildContext.Logger).Wait();
            });

            measureSession.LogMeasures(_buildContext.Logger);

            return buildContext;
        }

        /// <summary>
        /// Конфигурирование сервисов.
        /// </summary>
        /// <param name="buildContext">Контекст построения приложения.</param>
        public void ConfigureServices(BuildContext buildContext)
        {
            var logger = buildContext.Logger;
            logger.LogInformation("ConfigureServices started");

            StartupConfiguration startupConfiguration = buildContext.StartupConfiguration;
            ILoggerFactory loggerFactory = buildContext.LoggerFactory;
            var configurationRoot = buildContext.ConfigurationRoot;
            var services = buildContext.ServiceCollection;

            // Заменяем реализацию фабрики опций на свою.
            services.Add(ServiceDescriptor.Transient(typeof(IOptionsFactory<>), typeof(Configuration.OptionsFactory<>)));

            // Добавляем поддержку IOptions, IOptionsSnapshot, IOptionsMonitor
            services.AddOptions();

            // Logging
            services.RegisterLogging(loggerFactory);

            // Регистрируем строготипизированные конфигурации.
            services.RegisterConfigurationTypes(configurationRoot, buildContext.ExportedTypes, startupConfiguration);

            // Регистрируем типы по атрибуту [Register]
            services.RegisterWithRegisterAttribute(buildContext.ExportedTypes);

            // todo: зарегистрировать не исходные типы, а результирующий
            services.AddSingleton(buildContext.StartupConfiguration);
            services.AddSingleton(buildContext.StartupInfo);

            var modulesOptions = startupConfiguration.Modules;
            startupConfiguration.ConfigureModules(modulesOptions);

            // Регистрируем модули.
            List<Type> moduleTypes = new List<Type>();
            if (modulesOptions.AutoDiscoverModules)
            {
                moduleTypes = buildContext.ExportedTypes.GetClassTypesAssignableTo<IModule>().ToList();

                logger.LogInformation($"Found {moduleTypes.Count} modules:");
                foreach (var moduleType in moduleTypes)
                    logger.LogInformation($"Autodiscovered module: {moduleType.Name}");
            }

            var userDefinedModules = modulesOptions.ModuleTypes;
            if (userDefinedModules.Length > 0)
            {
                logger.LogInformation("UserDefinedModules modules:");
                foreach (var moduleType in userDefinedModules)
                    logger.LogInformation($"UserDefined module: {moduleType.Name}");
                moduleTypes.AddRange(userDefinedModules);
            }

            if (moduleTypes.Count > 0)
                services.RegisterModules(moduleTypes);

            logger.LogInformation("ConfigureServices finished");
        }

        /// <summary>
        /// Конфигурирование сервисов.
        /// </summary>
        /// <param name="buildContext">Контекст построения приложения.</param>
        /// <returns>Сконфигурированный <see cref="IServiceProvider"/></returns>
        public IServiceProvider ConfigureServicesExt(BuildContext buildContext)
        {
            var builder = buildContext.StartupConfiguration.ExternalBuilder;

            buildContext.Logger.LogInformation("ConfigureServicesExt started");

            // Регистрируем сервисы, переданные снаружи.
            builder.AddServices(buildContext.ServiceCollection);

            // Запускаем конфигурирование.
            var serviceProvider = builder.ConfigureServices(buildContext);

            buildContext.Logger.LogInformation("ConfigureServicesExt finished");

            return serviceProvider;
        }
    }
}
