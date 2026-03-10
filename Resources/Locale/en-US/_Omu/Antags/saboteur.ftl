# Glorious Death Reward Summary
saboteur-glorious-death-reward = [color=#eebb00]+{totalRep}[/color] reputation, [color=#eebb00]+{totalTc}[/color] telecrystals awarded for your glorious death.
# Saboteur antagonist localization strings

# Role
roles-antag-saboteur-name = Saboteur
roles-antag-saboteur-objective = Complete covert operations to build influence and undermine the station from within.
role-subtype-saboteur = Saboteur

# Tier 0 Objectives (Novice)
objective-saboteur-sabotage-comms = Sabotage Communications
objective-saboteur-sabotage-comms-desc = Strip the encryption keys from at least 50% of the station's telecom servers. No keys means no frequencies; the crew goes silent.

objective-saboteur-plant-evidence = Plant Evidence
objective-saboteur-plant-evidence-desc = Get {$target} implanted with Syndicate contraband and then flagged as Suspected in criminal records; by someone other than you. Let security do your dirty work.

objective-saboteur-disable-security = Disable Surveillance
objective-saboteur-disable-security-desc = Disable at least 30% of the station's surveillance cameras. Deactivated cameras count; smashing them works too.

objective-saboteur-lockdown-department = Lock Down {$department}
objective-saboteur-lockdown-department-desc = Bolt down at least 50% of the airlocks in {$department} (minimum 5 bolted doors).

objective-saboteur-emag-borg = Subvert a Borg
objective-saboteur-emag-borg-desc = Hack the metal. The station's own silicon, working for you.

# Tier 1 Objectives (Trusted)
objective-saboteur-frame-head = Frame Command Staff
objective-saboteur-frame-head-desc = Get {$targets} flagged in criminal records. Plant evidence, stage a crime; whatever it takes to make the charges stick.

objective-saboteur-sabotage-power-grid = Sabotage Power Grid
objective-saboteur-sabotage-power-grid-desc = Disable at least 30% of the station's APCs; trip their breakers or cut external power. Watch the lights go dark.

objective-saboteur-destroy-cargo-consoles = Destroy Cargo Consoles
objective-saboteur-destroy-cargo-consoles-desc = Destroy every cargo request computer on the station. No consoles, no supply orders, no lifeline.

objective-saboteur-sabotage-emergency = Sabotage Emergency Systems
objective-saboteur-sabotage-emergency-desc = Cut power to at least 30% of the station's fire alarms. When the fires start, nobody gets a warning.

objective-saboteur-manipulate-transfers = Manipulate Personnel Transfers
objective-saboteur-manipulate-transfers-desc = Get {$targets} reassigned to different roles. Use your connections to redraw the org chart.

# Tier 2 Objectives (Respected)
objective-saboteur-get-command-demoted = Get Command Staff Demoted
objective-saboteur-get-command-demoted-desc = Get at least 1 command staff member demoted out of their command position. Their ID should no longer reflect a command role.

objective-saboteur-take-department-control = Take Department Control
objective-saboteur-take-department-control-desc = Make at least 1 station announcement from a {$department} communications console. Speak with the authority of their department.

objective-saboteur-disable-gravity = Disable Gravity
objective-saboteur-disable-gravity-desc = Destroy every gravity generator on the station. Let them float.

objective-saboteur-hijack-budget = Hijack Department Budget
objective-saboteur-hijack-budget-desc = Drain any department's bank account to zero credits. Your own starting department's budget doesn't count; raid someone else's coffers.

objective-saboteur-chain-of-fools = Chain of Fools
objective-saboteur-chain-of-fools-desc = Have at least 2 crew members under your mind control simultaneously. Puppets on strings.

# Tier 3 Objectives (Ultimate)
objective-saboteur-seize-political-control = Seize Political Control
objective-saboteur-seize-political-control-desc = Become the sole holder of your department's head position; your ID must show the role, you must have a fake mindshield implant, and nobody else can hold that same title. Seize power from within.

objective-saboteur-install-syndicate-puppet = Install Syndicate Puppet
objective-saboteur-install-syndicate-puppet-desc = Get a mind-controlled crew member installed as Captain. They need a fake mindshield implant and their ID must show the Captain role. Your proxy in the big chair.

objective-saboteur-subvert-station-ai = Subvert Station AI
objective-saboteur-subvert-station-ai-desc = Upload new laws to the station AI to make it serve the Syndicate.

objective-saboteur-isolate-station = Isolate the Station
objective-saboteur-isolate-station-desc = Cut power or destroy every fax machine on the station AND destroy all CentComm encryption keys. No distress calls, no rescue, no one is coming.

objective-saboteur-authority-in-ruins = Authority in Ruins
objective-saboteur-authority-in-ruins-desc = Ensure agents with fake mindshield implants simultaneously hold the positions of Captain, Head of Security, and Head of Personnel. Total command takeover.

# DAGD Fallback
saboteur-glorious-death-assigned = [color=#ff4444][bold]FINAL DIRECTIVE:[/bold][/color] All operations exhausted. The Syndicate has issued your last order; [color=#ff4444]Die a Glorious Death[/color]. Go out swinging.
saboteur-no-objectives-remaining = [color=#eebb00]No further operations are available.[/color] Continue causing chaos at your discretion.

# Reputation Tier Announcements
saboteur-tier-up-detailed = [color=#33cc33]TIER ADVANCED:[/color] You are now [color=#eebb00]Tier {$tier}: {$tierName}[/color]. New operations and equipment unlocked. Next tier at [color=#eebb00]{$nextRep}[/color] reputation.
saboteur-tier-up-max = [color=#33cc33]TIER ADVANCED:[/color] You have reached [color=#eebb00]Tier {$tier}: {$tierName}[/color]. Maximum clearance achieved. All operations and equipment now available.
saboteur-tier-novice = Novice
saboteur-tier-trusted = Trusted
saboteur-tier-respected = Respected
saboteur-tier-ultimate = Ultimate

# Operation Completion
saboteur-operation-complete-detailed = [color=#33cc33]OPERATION COMPLETE:[/color] [color=#eebb00]+{$rep}[/color] reputation, [color=#eebb00]+{$tc}[/color] TC awarded. (Total: [color=#eebb00]{$totalRep}[/color] rep | {$tier})
saboteur-fallback-complete = [color=#33cc33]OBJECTIVE COMPLETE:[/color] [color=#eebb00]+{$rep}[/color] reputation, [color=#eebb00]+{$tc}[/color] TC awarded. (Total: [color=#eebb00]{$totalRep}[/color] rep | {$tier})

# Exposure
saboteur-exposed = [color=#ff4444]COVER COMPROMISED:[/color] Security has flagged you as [color=#eebb00]{$reason}[/color]. Reputation gains [color=#ff4444]permanently reduced by {$penalty}%[/color].

# Admin Verbs
admin-verb-make-saboteur = Make the target into a Saboteur.
admin-verb-text-make-saboteur = Make Saboteur

# Briefing
saboteur-role-greeting = You are a [color=#c93030]Syndicate deep-cover saboteur[/color]. Blend in with the crew while completing covert operations to earn [color=#eebb00]reputation[/color] and [color=#eebb00]telecrystals[/color]. Your uplink starts empty; complete your first objective to unlock purchases. If security flags you in criminal records, your reputation gains are [color=#ff4444]permanently reduced[/color] and the damage never recovers.
saboteur-role-uplink-implant = Your uplink was implanted. Access it from your hotbar.

# Round End
saboteur-round-end-agent-name = saboteur
saboteur-round-end-header = [color=#c93030]The saboteur was a Syndicate deep-cover operative.[/color]
saboteur-round-end-codewords = The codewords were: [color=#ffffff]{$codewords}[/color]
saboteur-round-end-stats = [color=#c93030]Tier {$tier} ({$tierName}) | Reputation: {$rep} | Operations completed: {$ops}[/color]
saboteur-round-end-exposed = [color=#ff4444]COVER BLOWN — Highest security status: {$status} (-{$penalty}% reputation gain)[/color]
saboteur-round-end-clean = [color=#33cc33]Cover maintained — never flagged by security.[/color]

# Briefing (chat — full color markup)
saboteur-briefing-codewords = Syndicate codewords: [color=#ffffff]{$codewords}[/color]. Fellow operatives will recognize these in conversation.
saboteur-briefing-stealth-warning = [color=#ff4444]Stay hidden[/color]; if security flags you in criminal records, your reputation gains are permanently reduced. The damage never recovers.
saboteur-briefing-tiers = Complete operations to earn [color=#eebb00]reputation[/color] and [color=#eebb00]telecrystals[/color]. Higher tiers unlock deadlier equipment and more impactful objectives.

# Briefing (character menu — no markup, rendered as plain text)
saboteur-briefing-intro-short = You are a Syndicate deep-cover saboteur. Blend in with the crew while completing covert operations.
saboteur-briefing-codewords-short = Syndicate codewords: {$codewords}.
saboteur-briefing-stealth-warning-short = Stay hidden; if security flags you in criminal records, your reputation gains are permanently reduced.
saboteur-briefing-tiers-short = Complete operations to earn reputation and telecrystals. Higher tiers unlock deadlier equipment.
saboteur-briefing-uplink-implant-short = Your uplink was implanted. Access it from your hotbar.

# Guidebook
guide-entry-saboteurs = Saboteurs
