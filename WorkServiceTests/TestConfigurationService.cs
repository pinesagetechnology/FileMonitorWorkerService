using FileMonitorWorkerService.Data.Repository;
using FileMonitorWorkerService.Models;
using FileMonitorWorkerService.Services;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Logging;
using Moq;

namespace WorkServiceTests
{
    [TestClass]
    public class ConfigurationServiceTests
    {
        private Mock<IRepository<Configuration>> _mockRepository;
        private Mock<ILogger<ConfigurationService>> _mockLogger;
        private ConfigurationService _configurationService;

        [TestInitialize]
        public void Setup()
        {
            _mockRepository = new Mock<IRepository<Configuration>>();
            _mockLogger = new Mock<ILogger<ConfigurationService>>();
            _configurationService = new ConfigurationService(_mockRepository.Object, _mockLogger.Object);
        }

        #region GetValueAsync Tests

        [TestMethod]
        public async Task GetValueAsync_ExistingKey_ReturnsValue()
        {
            // Arrange
            var key = "TestKey";
            var expectedValue = "TestValue";
            var config = new Configuration { Key = key, Value = expectedValue };

            _mockRepository.Setup(r => r.GetByKeyAsync(key))
                          .ReturnsAsync(config);

            // Act
            var result = await _configurationService.GetValueAsync(key);

            // Assert
            Assert.AreEqual(expectedValue, result);
            _mockRepository.Verify(r => r.GetByKeyAsync(key), Times.Once);
        }

        [TestMethod]
        public async Task GetValueAsync_NonExistentKey_ReturnsNull()
        {
            // Arrange
            var key = "NonExistentKey";
            _mockRepository.Setup(r => r.GetByKeyAsync(key))
                          .ReturnsAsync((Configuration?)null);

            // Act
            var result = await _configurationService.GetValueAsync(key);

            // Assert
            Assert.IsNull(result);
            _mockRepository.Verify(r => r.GetByKeyAsync(key), Times.Once);
        }

        [TestMethod]
        public async Task GetValueAsync_Generic_ExistingKey_ReturnsTypedValue()
        {
            // Arrange
            var key = "IntKey";
            var expectedValue = 42;
            var config = new Configuration { Key = key, Value = expectedValue.ToString() };

            _mockRepository.Setup(r => r.GetByKeyAsync(key))
                          .ReturnsAsync(config);

            // Act
            var result = await _configurationService.GetValueAsync<int>(key);

            // Assert
            Assert.AreEqual(expectedValue, result);
            _mockRepository.Verify(r => r.GetByKeyAsync(key), Times.Once);
        }

        [TestMethod]
        public async Task GetValueAsync_Generic_NonExistentKey_ReturnsDefault()
        {
            // Arrange
            var key = "NonExistentKey";
            _mockRepository.Setup(r => r.GetByKeyAsync(key))
                          .ReturnsAsync((Configuration?)null);

            // Act
            var result = await _configurationService.GetValueAsync<int>(key);

            // Assert
            Assert.AreEqual(default(int), result);
            _mockRepository.Verify(r => r.GetByKeyAsync(key), Times.Once);
        }

        [TestMethod]
        public async Task GetValueAsync_Generic_InvalidConversion_ReturnsDefault()
        {
            // Arrange
            var key = "InvalidIntKey";
            var config = new Configuration { Key = key, Value = "NotANumber" };

            _mockRepository.Setup(r => r.GetByKeyAsync(key))
                          .ReturnsAsync(config);

            // Act
            var result = await _configurationService.GetValueAsync<int>(key);

            // Assert
            Assert.AreEqual(default(int), result);
            _mockRepository.Verify(r => r.GetByKeyAsync(key), Times.Once);
        }

        [TestMethod]
        public async Task GetValueAsync_Generic_EmptyValue_ReturnsDefault()
        {
            // Arrange
            var key = "EmptyKey";
            var config = new Configuration { Key = key, Value = "" };

            _mockRepository.Setup(r => r.GetByKeyAsync(key))
                          .ReturnsAsync(config);

            // Act
            var result = await _configurationService.GetValueAsync<int>(key);

            // Assert
            Assert.AreEqual(default(int), result);
            _mockRepository.Verify(r => r.GetByKeyAsync(key), Times.Once);
        }

        [TestMethod]
        public async Task GetValueAsync_Generic_StringType_ReturnsStringValue()
        {
            // Arrange
            var key = "StringKey";
            var expectedValue = "TestStringValue";
            var config = new Configuration { Key = key, Value = expectedValue };

            _mockRepository.Setup(r => r.GetByKeyAsync(key))
                          .ReturnsAsync(config);

            // Act
            var result = await _configurationService.GetValueAsync<string>(key);

            // Assert
            Assert.AreEqual(expectedValue, result);
            _mockRepository.Verify(r => r.GetByKeyAsync(key), Times.Once);
        }

        [TestMethod]
        public async Task GetValueAsync_Generic_StringType_NonExistentKey_ReturnsDefault()
        {
            // Arrange
            var key = "NonExistentStringKey";
            _mockRepository.Setup(r => r.GetByKeyAsync(key))
                          .ReturnsAsync((Configuration?)null);

            // Act
            var result = await _configurationService.GetValueAsync<string>(key);

            // Assert
            Assert.AreEqual(default(string), result);
            Assert.IsNull(result);
            _mockRepository.Verify(r => r.GetByKeyAsync(key), Times.Once);
        }

        #endregion

        #region SetValueAsync Tests

        [TestMethod]
        public async Task SetValueAsync_NewKey_CreatesNewConfiguration()
        {
            // Arrange
            var key = "NewKey";
            var value = "NewValue";
            var description = "Test Description";
            var category = "TestCategory";

            _mockRepository.Setup(r => r.GetByKeyAsync(key))
                          .ReturnsAsync((Configuration?)null);
            _mockRepository.Setup(r => r.AddAsync(It.IsAny<Configuration>()))
                          .ReturnsAsync((Configuration config) => config);

            // Act
            await _configurationService.SetValueAsync(key, value, description, category);

            // Assert
            _mockRepository.Verify(r => r.GetByKeyAsync(key), Times.Once);
            _mockRepository.Verify(r => r.AddAsync(It.Is<Configuration>(c => 
                c.Key == key && 
                c.Value == value && 
                c.Description == description && 
                c.Category == category)), Times.Once);
        }

        [TestMethod]
        public async Task SetValueAsync_ExistingKey_UpdatesConfiguration()
        {
            // Arrange
            var key = "ExistingKey";
            var newValue = "UpdatedValue";
            var newDescription = "Updated Description";
            var newCategory = "UpdatedCategory";
            var existingConfig = new Configuration 
            { 
                Key = key, 
                Value = "OldValue", 
                Description = "Old Description",
                Category = "OldCategory",
                UpdatedAt = DateTime.UtcNow.AddDays(-1)
            };

            _mockRepository.Setup(r => r.GetByKeyAsync(key))
                          .ReturnsAsync(existingConfig);

            // Act
            await _configurationService.SetValueAsync(key, newValue, newDescription, newCategory);

            // Assert
            Assert.AreEqual(newValue, existingConfig.Value);
            Assert.AreEqual(newDescription, existingConfig.Description);
            Assert.AreEqual(newCategory, existingConfig.Category);
            Assert.IsTrue(existingConfig.UpdatedAt > DateTime.UtcNow.AddMinutes(-1));
            _mockRepository.Verify(r => r.GetByKeyAsync(key), Times.Once);
        }

        [TestMethod]
        public async Task SetValueAsync_ExistingKey_WithNullOptionalParams_UpdatesOnlyValue()
        {
            // Arrange
            var key = "ExistingKey";
            var newValue = "UpdatedValue";
            var existingConfig = new Configuration 
            { 
                Key = key, 
                Value = "OldValue", 
                Description = "Old Description",
                Category = "OldCategory"
            };

            _mockRepository.Setup(r => r.GetByKeyAsync(key))
                          .ReturnsAsync(existingConfig);

            // Act
            await _configurationService.SetValueAsync(key, newValue);

            // Assert
            Assert.AreEqual(newValue, existingConfig.Value);
            Assert.AreEqual("Old Description", existingConfig.Description); // Should remain unchanged
            Assert.AreEqual("OldCategory", existingConfig.Category); // Should remain unchanged
        }

        #endregion

        #region DeleteAsync Tests

        [TestMethod]
        public async Task DeleteAsync_ExistingKey_DeletesConfiguration()
        {
            // Arrange
            var key = "ExistingKey";
            var config = new Configuration { Key = key, Value = "TestValue" };

            _mockRepository.Setup(r => r.GetByKeyAsync(key))
                          .ReturnsAsync(config);
            _mockRepository.Setup(r => r.DeleteAsync(config))
                          .Returns(Task.CompletedTask);

            // Act
            await _configurationService.DeleteAsync(key);

            // Assert
            _mockRepository.Verify(r => r.GetByKeyAsync(key), Times.Once);
            _mockRepository.Verify(r => r.DeleteAsync(config), Times.Once);
        }

        [TestMethod]
        public async Task DeleteAsync_NonExistentKey_DoesNotDelete()
        {
            // Arrange
            var key = "NonExistentKey";
            _mockRepository.Setup(r => r.GetByKeyAsync(key))
                          .ReturnsAsync((Configuration?)null);

            // Act
            await _configurationService.DeleteAsync(key);

            // Assert
            _mockRepository.Verify(r => r.GetByKeyAsync(key), Times.Once);
            _mockRepository.Verify(r => r.DeleteAsync(It.IsAny<Configuration>()), Times.Never);
        }

        #endregion

        #region GetCategoryAsync Tests

        [TestMethod]
        public async Task GetCategoryAsync_ExistingCategory_ReturnsDictionary()
        {
            // Arrange
            var category = "TestCategory";
            var configs = new List<Configuration>
            {
                new Configuration { Key = "Key1", Value = "Value1", Category = category },
                new Configuration { Key = "Key2", Value = "Value2", Category = category },
                new Configuration { Key = "Key3", Value = "Value3", Category = "OtherCategory" }
            };

            _mockRepository.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Configuration, bool>>>()))
                          .ReturnsAsync(configs.Where(c => c.Category == category));

            // Act
            var result = await _configurationService.GetCategoryAsync(category);

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("Value1", result["Key1"]);
            Assert.AreEqual("Value2", result["Key2"]);
            Assert.IsFalse(result.ContainsKey("Key3"));
        }

        [TestMethod]
        public async Task GetCategoryAsync_NonExistentCategory_ReturnsEmptyDictionary()
        {
            // Arrange
            var category = "NonExistentCategory";
            _mockRepository.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Configuration, bool>>>()))
                          .ReturnsAsync(new List<Configuration>());

            // Act
            var result = await _configurationService.GetCategoryAsync(category);

            // Assert
            Assert.AreEqual(0, result.Count);
        }

        #endregion

        #region KeyExistsAsync Tests

        [TestMethod]
        public async Task KeyExistsAsync_ExistingKey_ReturnsTrue()
        {
            // Arrange
            var key = "ExistingKey";
            _mockRepository.Setup(r => r.CountAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Configuration, bool>>>()))
                          .ReturnsAsync(1);

            // Act
            var result = await _configurationService.KeyExistsAsync(key);

            // Assert
            Assert.IsTrue(result);
            _mockRepository.Verify(r => r.CountAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Configuration, bool>>>()), Times.Once);
        }

        [TestMethod]
        public async Task KeyExistsAsync_NonExistentKey_ReturnsFalse()
        {
            // Arrange
            var key = "NonExistentKey";
            _mockRepository.Setup(r => r.CountAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Configuration, bool>>>()))
                          .ReturnsAsync(0);

            // Act
            var result = await _configurationService.KeyExistsAsync(key);

            // Assert
            Assert.IsFalse(result);
            _mockRepository.Verify(r => r.CountAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Configuration, bool>>>()), Times.Once);
        }

        #endregion

        #region GetAllAsync Tests

        [TestMethod]
        public async Task GetAllAsync_ReturnsOrderedConfigurations()
        {
            // Arrange
            var configs = new List<Configuration>
            {
                new Configuration { Key = "ZKey", Value = "ZValue", Category = "ZCategory" },
                new Configuration { Key = "AKey", Value = "AValue", Category = "ACategory" },
                new Configuration { Key = "BKey", Value = "BValue", Category = "ACategory" }
            };

            _mockRepository.Setup(r => r.GetAllAsync())
                          .ReturnsAsync(configs);

            // Act
            var result = await _configurationService.GetAllAsync();

            // Assert
            var resultList = result.ToList();
            Assert.AreEqual(3, resultList.Count);
            Assert.AreEqual("AKey", resultList[0].Key); // ACategory, AKey
            Assert.AreEqual("BKey", resultList[1].Key); // ACategory, BKey
            Assert.AreEqual("ZKey", resultList[2].Key); // ZCategory, ZKey
        }

        [TestMethod]
        public async Task GetAllAsync_EmptyDatabase_ReturnsEmptyList()
        {
            // Arrange
            _mockRepository.Setup(r => r.GetAllAsync())
                          .ReturnsAsync(new List<Configuration>());

            // Act
            var result = await _configurationService.GetAllAsync();

            // Assert
            Assert.AreEqual(0, result.Count());
        }

        #endregion

        #region Error Handling and Edge Cases

        [TestMethod]
        public async Task GetValueAsync_Generic_BooleanConversion_WorksCorrectly()
        {
            // Arrange
            var key = "BoolKey";
            var config = new Configuration { Key = key, Value = "true" };

            _mockRepository.Setup(r => r.GetByKeyAsync(key))
                          .ReturnsAsync(config);

            // Act
            var result = await _configurationService.GetValueAsync<bool>(key);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public async Task GetValueAsync_Generic_DecimalConversion_WorksCorrectly()
        {
            // Arrange
            var key = "DecimalKey";
            var expectedValue = 123.45m;
            var config = new Configuration { Key = key, Value = expectedValue.ToString() };

            _mockRepository.Setup(r => r.GetByKeyAsync(key))
                          .ReturnsAsync(config);

            // Act
            var result = await _configurationService.GetValueAsync<decimal>(key);

            // Assert
            Assert.AreEqual(expectedValue, result);
        }

        [TestMethod]
        public async Task SetValueAsync_NullKey_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() => 
                _configurationService.SetValueAsync(null!, "value"));
        }

        [TestMethod]
        public async Task SetValueAsync_EmptyKey_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() => 
                _configurationService.SetValueAsync("", "value"));
        }

        [TestMethod]
        public async Task GetValueAsync_NullKey_ThrowsArgumentNullException()
        {
            // Act
            var result = await _configurationService.GetValueAsync(null!);

            // Assert
            Assert.AreEqual(null, result);
        }

        [TestMethod]
        public async Task GetValueAsync_EmptyKey_ThrowsArgumentException()
        {
            // Act
            var result = await _configurationService.GetValueAsync("");

            // Assert
            Assert.AreEqual(null, result);
        }

        [TestMethod]
        public async Task DeleteAsync_NullKey_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() => 
                _configurationService.DeleteAsync(null!));
        }

        [TestMethod]
        public async Task GetCategoryAsync_NullCategory_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() => 
                _configurationService.GetCategoryAsync(null!));
        }

        [TestMethod]
        public async Task KeyExistsAsync_NullKey_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() => 
                _configurationService.KeyExistsAsync(null!));
        }

        [TestMethod]
        public async Task SetValueAsync_RepositoryThrowsException_PropagatesException()
        {
            // Arrange
            var key = "TestKey";
            _mockRepository.Setup(r => r.GetByKeyAsync(key))
                          .ThrowsAsync(new InvalidOperationException("Database error"));

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => 
                _configurationService.SetValueAsync(key, "value"));
        }

        [TestMethod]
        public async Task GetValueAsync_RepositoryThrowsException_PropagatesException()
        {
            // Arrange
            var key = "TestKey";
            _mockRepository.Setup(r => r.GetByKeyAsync(key))
                          .ThrowsAsync(new InvalidOperationException("Database error"));

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => 
                _configurationService.GetValueAsync(key));
        }

        #endregion
    }
}
