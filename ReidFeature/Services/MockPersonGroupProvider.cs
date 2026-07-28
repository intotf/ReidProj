using ReidFeature.Payloads;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ReidFeature.Services
{
    sealed class MockPersonGroupProvider : IPersonGroupProvider
    {
        private readonly Dictionary<string, Person[]> _groups;

        public MockPersonGroupProvider(DetectService detectService)
        {
            var groups = new Dictionary<string, Person[]>();
            foreach (var groupDir in Directory.GetDirectories("datas/persongroups"))
            {
                var groupId = Path.GetFileName(groupDir);
                var personList = new List<Person>();

                foreach (var personDir in Directory.GetDirectories(groupDir))
                {
                    var personId = Path.GetFileName(personDir);
                    var reidFeatures = new Dictionary<string, byte[]>();

                    foreach (var imageFile in Directory.GetFiles(personDir, "*.*"))
                    {
                        using var image = Image.Load<Rgb24>(imageFile);
                        foreach (var detection in detectService.DetectPersons(image, DetectionFlags.All))
                        {
                            reidFeatures[Path.GetFileName(imageFile)] = detection.Features;
                            break;
                        }
                    }

                    var person = new Person(personId, groupId, personId, reidFeatures);
                    personList.Add(person);
                }
				
                groups[groupId] = personList.ToArray();
				
            }

            _groups = groups;
        }


        public Task<Person[]> GetPersonsAsync(string groupId, CancellationToken cancellationToken)
        {
            return this._groups.TryGetValue(groupId, out var persons)
                ? Task.FromResult(persons)
                : Task.FromResult(Array.Empty<Person>());
        }
    }
}
