from dataclasses import dataclass

from Options import Choice, DeathLink, DefaultOnToggle, PerGameCommonOptions, Range, Toggle


class ShopSlots(Range):
    """Number of one-time slots in the Witch's Market (opened at save points) that hold multiworld items."""
    display_name = "Shop Slots"
    range_start = 0
    range_end = 20
    default = 10


class BossDrops(DefaultOnToggle):
    """Boss first-kill drops (boss Idea items, Candore's weapon) are locations."""
    display_name = "Boss Drops"


class NpcRewards(DefaultOnToggle):
    """Item rewards from NPCs and events (Suzune, town NPCs, the Ranch event, the village witch) are locations."""
    display_name = "NPC Rewards"


class Trades(DefaultOnToggle):
    """Vendetta's trades are locations. Their costs (boss Idea items + Idea) stay vanilla."""
    display_name = "Trades"


class ShuffleKeys(Toggle):
    """Shuffle the three door keys (Blackened Key, Pilgrim's Path Key, Rooftop Chamber Key) into the multiworld.
    Logic for key doors is conservative (the whole key-door area requires its key). Experimental: if you get stuck,
    the in-game console (` key) can give items."""
    display_name = "Shuffle Keys"


class EnemyRandomizer(DefaultOnToggle):
    """Regular enemies are replaced with random enemies from the whole game (bosses are not affected)."""
    display_name = "Enemy Randomizer"


class TrapPercentage(Range):
    """Percentage of filler items (Idea clusters, consumables) replaced by traps:
    Ambush Trap (a random enemy appears next to you), Idea Drain Trap (lose 20% of carried Idea),
    Knockdown Trap (you are knocked down)."""
    display_name = "Trap Percentage"
    range_start = 0
    range_end = 50
    default = 10


class LewdTraps(DefaultOnToggle):
    """Add lewd status traps to the trap pool: Aphrodisiac Trap (fills the Pleasure Paralysis meter, like the pink breath
    of some enemies). Only matters when trap_percentage is above 0."""
    display_name = "Lewd Traps"


class DeathLinkMode(Choice):
    """What an incoming DeathLink does.
    defeat_state: Aradia is downed and cannot get up until an enemy catches her or you choose to give up.
    instant_respawn: immediate game over and respawn at the last save point."""
    display_name = "DeathLink Mode"
    option_defeat_state = 0
    option_instant_respawn = 1
    default = 0


@dataclass
class NightOfRevengeOptions(PerGameCommonOptions):
    shop_slots: ShopSlots
    boss_drops: BossDrops
    npc_rewards: NpcRewards
    trades: Trades
    shuffle_keys: ShuffleKeys
    enemy_randomizer: EnemyRandomizer
    trap_percentage: TrapPercentage
    lewd_traps: LewdTraps
    death_link: DeathLink
    death_link_mode: DeathLinkMode
