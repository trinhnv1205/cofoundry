﻿using Cofoundry.Core.AutoUpdate;
using Cofoundry.Domain.Data;
using Cofoundry.Domain.Tests.Integration.SeedData;
using Cofoundry.Domain.Tests.Shared.Mocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Cofoundry.Domain.Tests.Integration
{
    /// <summary>
    /// Used to set up and reset a test database between
    /// test runs.
    /// </summary>
    public class TestDatabaseInitializer
    {
        private readonly IServiceProvider _serviceProvider;

        public TestDatabaseInitializer(
            IServiceProvider serviceProvider
            )
        {
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Runs the Cofoundry auto update service to initialize
        /// Cofoundry and any other detected modules.
        /// </summary>
        public async Task InitializeCofoundry()
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var autoUpdateService = scope.ServiceProvider.GetRequiredService<IAutoUpdateService>();
                await autoUpdateService.UpdateAsync();
            }
        }

        /// <summary>
        /// Resets the database test data between test runs, leaving any
        /// definitions or global test entities in place.
        /// </summary>
        public async Task DeleteTestData()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CofoundryDbContext>();

            // reset data between tests to baseline
            await dbContext.Database.ExecuteSqlRawAsync(@"
                delete from Cofoundry.CustomEntity
                delete from Cofoundry.DocumentAsset
                delete from Cofoundry.DocumentAssetGroup
                delete from Cofoundry.FailedAuthenticationAttempt
                delete from Cofoundry.ImageAsset
                delete from Cofoundry.ImageAssetGroup
                delete from Cofoundry.[Page]
                delete from Cofoundry.PageGroup
                delete from Cofoundry.PageDirectory
                delete from Cofoundry.PageTemplate where [FileName] not in ('TestTemplate', 'TestCustomEntityTemplate')
                delete from Cofoundry.RewriteRule
                delete from Cofoundry.Tag
                delete from Cofoundry.UserLoginLog
                delete from Cofoundry.UserPasswordResetRequest
                delete Cofoundry.[User] where IsSystemAccount = 0 and [Username] <> 'admin@example.com'
                delete from Cofoundry.[Role] where RoleCode is null
                delete from Cofoundry.UnstructuredDataDependency
            ");
        }

        /// <summary>
        /// Initializes any static/global test data references. These references can
        /// be used for convenience in multiple tests as references but should not be altered.
        /// </summary>
        public async Task<SeededEntities> SeedGlobalEntities()
        {
            var seededEntities = new SeededEntities();

            using var scope = _serviceProvider.CreateScope();

            var contentRepository = scope
                .ServiceProvider
                .GetRequiredService<IAdvancedContentRepository>()
                .WithElevatedPermissions();

            var dbContext = scope.ServiceProvider.GetRequiredService<CofoundryDbContext>();

            // Setup

            var settings = await contentRepository.ExecuteQueryAsync(new GetSettingsQuery<InternalSettings>());
            if (!settings.IsSetup)
            {
                await contentRepository.ExecuteCommandAsync(new SetupCofoundryCommand()
                {
                    ApplicationName = "Test Site",
                    UserEmail = seededEntities.AdminUser.Username,
                    UserFirstName = "Test",
                    UserLastName = "Admin",
                    UserPassword = seededEntities.AdminUser.Password
                });
            }

            // Users

            seededEntities.AdminUser.UserId = await dbContext
                .Users
                .AsNoTracking()
                .Where(u => u.Username == seededEntities.AdminUser.Username)
                .Select(u => u.UserId)
                .SingleAsync();

            // Page Templates

            var testTemplate = await dbContext
                .PageTemplates
                .AsNoTracking()
                .Include(t => t.PageTemplateRegions)
                .Where(t => t.FileName == "TestTemplate")
                .SingleAsync();

            seededEntities.TestPageTemplate.PageTemplateId = testTemplate.PageTemplateId;
            seededEntities.TestPageTemplate.BodyPageTemplateRegionId = testTemplate
                .PageTemplateRegions
                .Select(t => t.PageTemplateRegionId)
                .Single();

            var testCustomEntityPageTemplate = await dbContext
                .PageTemplates
                .AsNoTracking()
                .Include(t => t.PageTemplateRegions)
                .Where(t => t.FileName == "TestCustomEntityTemplate")
                .SingleAsync();

            seededEntities.TestCustomEntityPageTemplate.PageTemplateId = testCustomEntityPageTemplate.PageTemplateId;
            seededEntities.TestCustomEntityPageTemplate.BodyPageTemplateRegionId = testCustomEntityPageTemplate
                .PageTemplateRegions
                .Where(t => !t.IsCustomEntityRegion)
                .Select(t => t.PageTemplateRegionId)
                .Single();
            seededEntities.TestCustomEntityPageTemplate.CustomEntityBodyPageTemplateRegionId = testCustomEntityPageTemplate
                .PageTemplateRegions
                .Where(t => t.IsCustomEntityRegion)
                .Select(t => t.PageTemplateRegionId)
                .Single();

            // Images

            var mockImageAssetFileService = scope.ServiceProvider.GetService<IImageAssetFileService>() as MockImageAssetFileService;
            mockImageAssetFileService.SaveFile = false;
            mockImageAssetFileService.WidthInPixels = 80;
            mockImageAssetFileService.HeightInPixels = 80;

            seededEntities.TestImageId = await contentRepository
                .ImageAssets()
                .AddAsync(new AddImageAssetCommand()
                {
                    Title = "Test Image",
                    File = new EmbeddedResourceFileSource(this.GetType().Assembly, "Cofoundry.Domain.Tests.Integration.Shared.SeedData.Images", "Test jpg 80x80.jpg")
                });

            // Tags

            var testTag = new Tag()
            {
                TagText = seededEntities.TestTag.TagText,
                CreateDate = DateTime.UtcNow
            };

            dbContext.Tags.Add(testTag);
            await dbContext.SaveChangesAsync();

            seededEntities.TestTag.TagId = testTag.TagId;

            // Directories

            seededEntities.RootDirectoryId = await dbContext
                .PageDirectories
                .AsNoTracking()
                .Where(d => !d.ParentPageDirectoryId.HasValue)
                .Select(d => d.PageDirectoryId)
                .SingleAsync();

            seededEntities.TestDirectory.PageDirectoryId = await contentRepository
                .PageDirectories()
                .AddAsync(new AddPageDirectoryCommand()
                {
                    Name = "Test Directory",
                    ParentPageDirectoryId = seededEntities.RootDirectoryId,
                    UrlPath = seededEntities.TestDirectory.UrlPath
                });

            // Pages

            seededEntities.TestDirectory.GenericPage.PageId = await contentRepository
                .Pages()
                .AddAsync(new AddPageCommand()
                {
                    Title = seededEntities.TestDirectory.GenericPage.Title,
                    PageType = PageType.Generic,
                    PageDirectoryId = seededEntities.TestDirectory.PageDirectoryId,
                    UrlPath = seededEntities.TestDirectory.GenericPage.UrlPath,
                    PageTemplateId = seededEntities.TestPageTemplate.PageTemplateId,
                    Publish = true
                });

            seededEntities.TestDirectory.CustomEntityPage.PageId = await contentRepository
                .Pages()
                .AddAsync(new AddPageCommand()
                {
                    Title = seededEntities.TestDirectory.CustomEntityPage.Title,
                    PageType = PageType.CustomEntityDetails,
                    PageDirectoryId = seededEntities.TestDirectory.PageDirectoryId,
                    CustomEntityRoutingRule = seededEntities.TestDirectory.CustomEntityPage.RoutingRule.RouteFormat,
                    PageTemplateId = seededEntities.TestCustomEntityPageTemplate.PageTemplateId,
                    Publish = true
                });

            // Custom Entities

            seededEntities.TestCustomEntity.CustomEntityId = await contentRepository
                .CustomEntities()
                .AddAsync(new AddCustomEntityCommand()
                {
                    CustomEntityDefinitionCode = seededEntities.TestCustomEntityDefinition.CustomEntityDefinitionCode,
                    Model = new TestCustomEntityDataModel(),
                    Publish = true,
                    Title = seededEntities.TestCustomEntity.Title,
                    UrlSlug = seededEntities.TestCustomEntity.UrlSlug
                });

            // User areas

            seededEntities.TestUserArea1.RoleId = await dbContext
                .Roles
                .FilterByRoleCode(seededEntities.TestUserArea1.RoleCode)
                .Select(c => c.RoleId)
                .SingleAsync();

            await InitUserAreaAsync(seededEntities.TestUserArea1, dbContext, contentRepository);
            await InitUserAreaAsync(seededEntities.TestUserArea2, dbContext, contentRepository);

            return seededEntities;
        }

        private async Task InitUserAreaAsync(
            TestUserAreaInfo testUserAreaInfo, 
            CofoundryDbContext dbContext,
            IAdvancedContentRepository contentRepository
            )
        {
            testUserAreaInfo.RoleId = await dbContext
                .Roles
                .FilterByRoleCode(testUserAreaInfo.RoleCode)
                .Select(c => c.RoleId)
                .SingleAsync();

            var uniqueIdentifier = testUserAreaInfo.UserAreaCode + testUserAreaInfo.RoleId;
            testUserAreaInfo.User = new TestUserInfo()
            {
                Username = uniqueIdentifier.ToLower() + "@example.com",
                Password = uniqueIdentifier + "w1P1r4Rz"
            };

            testUserAreaInfo.User.UserId = await contentRepository
                .Users()
                .AddAsync(new AddUserCommand()
                {
                    Email = testUserAreaInfo.User.Username,
                    FirstName = "Role",
                    LastName = "User",
                    Password = testUserAreaInfo.User.Password,
                    RoleId = testUserAreaInfo.RoleId,
                    UserAreaCode = testUserAreaInfo.UserAreaCode
                });
        }
    }
}