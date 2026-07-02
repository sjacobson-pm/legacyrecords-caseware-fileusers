using System.Diagnostics.CodeAnalysis;

namespace LegacyRecordsCaseWareFileUsers.Helpers;

/// <summary>
///     Curated word lists used to compose a memorable, filename-safe run identifier of the form
///     <c>{adjective}-{animal}-{verb}</c> (for example, <c>swift-otter-runs</c>). The three lists are
///     independent; the total number of unique combinations is
///     <see cref="Adjectives" />.<c>Length</c> × <see cref="Animals" />.<c>Length</c> ×
///     <see cref="Verbs" />.<c>Length</c>.
///     <para>
///         Words are lowercase, alphanumeric only, unambiguous in spelling, and free of
///         negative connotations. This keeps run IDs safe to embed in file paths and in log lines,
///         easy to read aloud, and easy to type back into the <c>--resume</c> flag without ambiguity.
///     </para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Static curated data.")]
internal static class RunIdWords
{
    // 156 curated adjectives — character, motion, weather, texture, color, tone.
    public static readonly string[] Adjectives =
    {
        "agile", "airy", "alert", "amber", "amiable", "ample", "ancient", "arctic", "ardent",
        "aromatic", "auburn", "azure", "balmy", "beaming", "big", "blithe", "blue", "bold", "boreal",
        "brave", "breezy", "bright", "brisk", "bronze", "bubbly", "burly", "busy", "calm", "candid",
        "cedar", "cerulean", "cheerful", "cheery", "chipper", "chirpy", "civic", "classic", "clean",
        "clear", "clever", "coastal", "compact", "coral", "cordial", "cosy", "cozy", "crimson",
        "crisp", "curious", "daring", "dawn", "deft", "dewy", "diligent", "distant", "dreamy",
        "dusky", "dusty", "eager", "earnest", "easy", "ebony", "elder", "electric", "emerald",
        "empathic", "epic", "even", "fair", "faithful", "famous", "fancy", "feathered", "fern",
        "fierce", "flame", "fleet", "fluffy", "flying", "forest", "frank", "friendly", "frosty",
        "gallant", "gentle", "genuine", "glad", "glassy", "gleaming", "gliding", "glossy", "golden",
        "graceful", "grand", "granite", "grateful", "great", "green", "handy", "happy", "hardy",
        "harmonic", "hazy", "hearty", "helpful", "high", "honest", "humble", "indigo", "inky",
        "iron", "ivory", "jade", "jaunty", "jolly", "jovial", "joyful", "keen", "kind", "kindred",
        "leafy", "lively", "loyal", "lucky", "lunar", "lyric", "magenta", "maple", "meadow",
        "mellow", "merry", "mighty", "mint", "misty", "modest", "moonlit", "mossy", "nifty",
        "nimble", "noble", "northern", "opal", "orange", "peppy", "placid", "plucky", "polar",
        "polished", "prairie", "prompt", "proud", "purple", "quick", "quiet", "radiant", "rapid",
        "ready", "red", "regal", "rested", "robust", "rosy", "rugged", "rustic", "sable", "sandy",
        "sapphire", "scarlet", "seaside", "sepia", "serene", "sharp", "shining", "silent", "silken",
        "silver", "sincere", "sleek", "slender", "snappy", "snowy", "soft", "solar", "solid",
        "sound", "southern", "sparkling", "spirited", "spiral", "spring", "spry", "starlit",
        "steady", "steel", "stellar", "stoic", "storied", "strong", "sturdy", "sunny", "sunset",
        "supple", "swift", "tangerine", "tawny", "teal", "tender", "tidy", "tranquil", "trim",
        "true", "trusty", "twinkling", "unblemished", "unhurried", "upbeat", "urban", "valiant",
        "verdant", "vibrant", "violet", "vivid", "warm", "wavy", "welcome", "wild", "windy",
        "winged", "winsome", "wintry", "wise", "witty", "woolen", "young", "zesty", "zippy",
    };

    // 156 curated animals — mammals, birds, fish, insects, reptiles. All common English words.
    public static readonly string[] Animals =
    {
        "alpaca", "antelope", "auk", "badger", "barnacle", "bat", "beagle", "bear", "beaver",
        "beetle", "bison", "bluejay", "bobcat", "buffalo", "bumblebee", "bunny", "butterfly",
        "camel", "canary", "capybara", "cardinal", "caribou", "carp", "cat", "caterpillar",
        "chameleon", "cheetah", "chickadee", "chinchilla", "chipmunk", "clam", "cobra", "condor",
        "cougar", "cow", "coyote", "crab", "crane", "cricket", "crow", "cuckoo", "deer", "dingo",
        "dolphin", "donkey", "dormouse", "dove", "dragonfly", "duck", "eagle", "eel", "egret",
        "elk", "emu", "falcon", "fawn", "ferret", "finch", "firefly", "flamingo", "flounder",
        "fox", "frog", "gazelle", "gecko", "gerbil", "giraffe", "goat", "goldfinch", "goose",
        "gopher", "grasshopper", "grouse", "gull", "hamster", "hare", "hawk", "hedgehog", "heron",
        "herring", "hippo", "horse", "hummingbird", "ibex", "iguana", "impala", "jaguar", "jay",
        "kangaroo", "kestrel", "kingfisher", "kiwi", "koala", "kookaburra", "krill", "ladybug",
        "lamb", "lark", "lemming", "lemur", "leopard", "lily", "llama", "lobster", "lynx",
        "magpie", "manatee", "marmot", "meerkat", "mink", "minnow", "mockingbird", "mole",
        "mongoose", "moose", "moth", "mouse", "mule", "narwhal", "newt", "nightingale", "ocelot",
        "octopus", "orca", "oriole", "osprey", "otter", "owl", "ox", "oyster", "panda", "pangolin",
        "panther", "parakeet", "parrot", "partridge", "peacock", "pelican", "penguin", "perch",
        "pheasant", "pigeon", "platypus", "polar", "pony", "porcupine", "possum", "prawn",
        "puffin", "puma", "python", "quail", "quokka", "rabbit", "raccoon", "ram", "raven",
        "reindeer", "roadrunner", "robin", "salamander", "salmon", "sandpiper", "sardine",
        "seagull", "seal", "shark", "sheep", "shrimp", "skink", "skylark", "slug", "snail",
        "snake", "snapper", "sparrow", "squid", "squirrel", "starfish", "starling", "stork",
        "swallow", "swan", "tanager", "tapir", "tern", "thrush", "tiger", "toad", "toucan",
        "trout", "tuna", "turkey", "turtle", "walrus", "warbler", "wasp", "weasel", "whale",
        "wombat", "woodpecker", "wren", "yak", "zebra",
    };

    // 116 curated third-person-singular verbs — motion, behavior, sound.
    public static readonly string[] Verbs =
    {
        "ambles", "arrives", "barks", "basks", "bounds", "builds", "calls", "carries", "chatters",
        "cheers", "chirps", "circles", "climbs", "clicks", "coos", "crawls", "creates", "crosses",
        "dances", "dashes", "darts", "delivers", "digs", "dips", "dives", "dreams", "drifts",
        "eats", "embarks", "embraces", "explores", "faces", "finds", "fishes", "flies", "flows",
        "flutters", "follows", "forages", "frolics", "gathers", "gazes", "gleams", "glides",
        "gossips", "grazes", "greets", "grows", "guides", "hikes", "hides", "hoots", "hops",
        "howls", "hums", "hunts", "hurries", "invites", "jogs", "journeys", "jumps", "keeps",
        "kicks", "laughs", "leaps", "learns", "lingers", "listens", "meanders", "melds", "muses",
        "nests", "notes", "observes", "orbits", "paces", "paints", "pauses", "peers", "perches",
        "pines", "pipes", "plays", "prances", "prowls", "pursues", "purrs", "races", "reaches",
        "reads", "rests", "returns", "rises", "roams", "roars", "rolls", "runs", "sails",
        "sashays", "scampers", "scouts", "searches", "seeks", "sings", "skips", "sleeps", "slides",
        "smiles", "soars", "splashes", "sprints", "stalks", "starts", "stays", "steers", "steps",
        "strides", "strolls", "studies", "sweeps", "swims", "swoops", "thinks", "tiptoes",
        "trails", "travels", "trots", "tumbles", "vaults", "visits", "wades", "waits", "walks",
        "wanders", "watches", "waves", "weaves", "welcomes", "whispers", "whistles", "wiggles",
        "wings", "winks", "wonders", "yawns", "zigzags", "zooms",
    };
}
