// Copyright (c) Brock Allen & Dominick Baier. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.


using Arch.IS4Host.Data;
using Arch.IS4Host.Models;
using IdentityServer4.EntityFramework.DbContexts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Reflection;
using IdentityServer4.EntityFramework.Mappers;

namespace Arch.IS4Host
{
    public class Startup
    {
        public IConfiguration Configuration { get; }
        public IHostingEnvironment Environment { get; }

        public Startup(IConfiguration configuration, IHostingEnvironment environment)
        {
            Configuration = configuration;
            Environment = environment;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            // 默认用的 sqlitedb，换为 PostgreSQL DB; connectionstring 在 appsettings.json 文件中配置
            var connectionString = Configuration.GetConnectionString("DefaultConnection");
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(connectionString));

            services.AddIdentity<ApplicationUser, IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();

            services.AddMvc();

            services.Configure<IISOptions>(iis =>
            {
                iis.AuthenticationDisplayName = "Windows";
                iis.AutomaticAuthentication = false;
            });

            // 获取当前 Assembly; GetTypeInfo 是扩展方法，需要引用命名空间[System.Reflection]
            var migrationAssembly = typeof(Startup).GetTypeInfo().Assembly.GetName().Name;
            var builder = services.AddIdentityServer()
                // 从内存数据库改为配置的 PostgreSQL => 存储 Configuratioin Data
                .AddConfigurationStore(configDb=>{
                    configDb.ConfigureDbContext = db => db.UseNpgsql(connectionString,
                    sql => sql.MigrationsAssembly(migrationAssembly));
                })
                // 使用 PostgreSQL db 存储 Operational Data
                .AddOperationalStore(operationalDb =>
                {
                    operationalDb.ConfigureDbContext = db => db.UseNpgsql(connectionString,
                        sql => sql.MigrationsAssembly(migrationAssembly));
                })
                .AddAspNetIdentity<ApplicationUser>();

            if (Environment.IsDevelopment())
            {
                builder.AddDeveloperSigningCredential();
            }
            else
            {
                // Producttion Env needs to configure a key, discuss later
                throw new Exception("need to configure key material");
            }

            services.AddAuthentication()
                .AddGoogle(options =>
                {
                    options.ClientId = "708996912208-9m4dkjb5hscn7cjrn5u0r4tbgkbj1fko.apps.googleusercontent.com";
                    options.ClientSecret = "wdfPY6t8H8cecgjlxud__4Gh";
                });
        }

        public void Configure(IApplicationBuilder app)
        {
            // Call db initialize method
            InitializeDatabase(app);

            if (Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseDatabaseErrorPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            app.UseStaticFiles();
            app.UseIdentityServer();
            app.UseMvcWithDefaultRoute();
        }

        private void InitializeDatabase(IApplicationBuilder app)
        {
            // using a service scope
            using (var serviceScope = app.ApplicationServices.GetService<IServiceScopeFactory>().CreateScope())
            {
                // Create PersistedGrant database if not exist, then do migration
                serviceScope.ServiceProvider.GetRequiredService<PersistedGrantDbContext>().Database.Migrate();

                var configDbContext = serviceScope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();
                configDbContext.Database.Migrate();

                // Seed data if not exist any
                if (!configDbContext.Clients.Any())
                {
                    foreach (var client in Config.GetClients())
                    {
                        configDbContext.Clients.Add(client.ToEntity());
                    }
                    configDbContext.SaveChanges();
                }

                if (!configDbContext.IdentityResources.Any())
                {
                    foreach (var res in Config.GetIdentityResources())
                    {
                        configDbContext.IdentityResources.Add(res.ToEntity());
                    }
                    configDbContext.SaveChanges();
                }

                if (!configDbContext.ApiResources.Any())
                {
                    foreach (var api in Config.GetApis())
                    {
                        configDbContext.ApiResources.Add(api.ToEntity());
                    }
                    configDbContext.SaveChanges();
                }
            }
        }
    }
}
