#!/usr/bin/env perl
# Lists every game member PeakXR resolves by name (patch targets, GameMember/AccessTools lookups,
# Traverse fields) and checks each one against a decompile folder.
# usage: compat-targets.pl <src dir> <decomp dir> [baseline decomp dir]
# With a baseline (the decompile the mod was last tested on), also flags members whose
# signature or method body changed: the patches to re-read after a game update.
# Exit status: 1 when a target is missing, ambiguous or its parameter types no longer match.
use strict;
use warnings;
use File::Find;

my ($src, $new, $old) = @ARGV;
die "usage: $0 <src dir> <decomp dir> [baseline decomp dir]\n" unless $src && $new && -d $new;

my $NAME = qr/(?:"(\w+)"|nameof\(\s*([\w.]+)\s*\))/;    # "Name" or nameof(Type.Name)
my $TYPE = qr/typeof\(\s*([\w.<>, ]+?)\s*\)/;

sub files_under {
    my ($dir, $re) = @_;
    my @f;
    find(sub { push @f, $File::Find::name if /$re/ && -f }, $dir);
    return sort @f;
}

sub slurp { local (@ARGV, $/) = @_; return <> }

sub last_segment { my $s = shift; $s =~ s/<.*//s; $s =~ s/.*\.//; return $s }

# --- 1. targets from the mod's source -------------------------------------------------------
my @targets;    # [kind, type, member, \@params or undef, "file:line"]
for my $file (files_under($src, qr/\.cs$/)) {
    my $text = slurp($file);
    (my $rel = $file) =~ s{^\Q$src\E/?}{};
    my %alias = $text =~ /^\s*using\s+(\w+)\s*=\s*([\w.]+)\s*;/mg;
    my $resolve = sub { my $t = shift; $t =~ s/^(\w+)/$alias{$1} \/\/ $1/e; $t };
    my $at = sub { my $pos = shift; $rel . ':' . (1 + (substr($text, 0, $pos) =~ tr/\n//)) };
    my $add = sub {
        my ($kind, $type, $member, $params, $pos) = @_;
        if (defined $member) {
            $member = last_segment($member);
            $member =~ s/^[gs]et_//;    # property accessors are declared as the property
        }
        push @targets, [ $kind, $resolve->($type), $member, $params, $at->($pos) ];
    };
    my $params = sub {
        my $rest = shift;
        return [] if defined $rest && $rest =~ /^\s*,\s*Type\.EmptyTypes/;
        return undef
          unless defined $rest && $rest =~ /^\s*,\s*(?:new\s*(?:Type)?\s*\[\s*\]\s*\{([^}]*)\}|\[([^\]]*)\])/s;
        my $list = $1 // $2;
        my @p;
        while ($list =~ /$TYPE(\s*\.\s*MakeByRefType\(\))?/g) { push @p, last_segment($resolve->($1)) . ($2 ? '&' : '') }
        return \@p;
    };
    # GameMember.Method(typeof(T), "N"[, new[] {...}]), GameMember.Delegate<D>(...), AccessTools.Method/Property*(...)
    while ($text =~ /\b(?:GameMember\.(?:Method|Delegate<[^(]*>)|AccessTools\.(?:Method|DeclaredMethod|PropertyGetter|PropertySetter|Field))\(\s*$TYPE\s*,\s*$NAME/g) {
        my ($type, $member, $pos) = ($1, $2 // $3, $-[0]);
        my $tail = substr($text, $+[0], 400);
        $add->('method', $type, $member, $params->($tail), $pos);
    }
    # GameMember.Field<T, F>("n"), GameMember.StaticField<T, F>("n")
    while ($text =~ /\bGameMember\.(?:Static)?Field<\s*([\w.]+)\s*,[^(]*\(\s*$NAME/g) {
        $add->('field', $1, $2 // $3, undef, $-[0]);
    }
    # Traverse.Create<T>().Field("n")
    while ($text =~ /\bTraverse\.Create<\s*([\w.]+)\s*>\(\)\s*\.\s*(?:Field|Property|Method)\(\s*$NAME/g) {
        $add->('field', $1, $2 // $3, undef, $-[0]);
    }
    # [HarmonyPatch(typeof(T), "N")]
    while ($text =~ /\[HarmonyPatch\(\s*$TYPE\s*,\s*$NAME/g) {
        $add->('method', $1, $2 // $3, undef, $-[0]);
    }
    # GameMember.Type("T"), AccessTools.TypeByName("T")
    while ($text =~ /\b(?:GameMember\.Type|AccessTools\.TypeByName)\(\s*"([\w.+]+)"/g) {
        $add->('type', $1, undef, undef, $-[0]);
    }
    # photonView.RPC("Name", ...): the game RPC must keep its name and signature (vanilla MP compat).
    while ($text =~ /\.RPC\(\s*"(\w+)"/g) {
        $add->('rpc', '*', $1, undef, $-[0]);
    }
    # Harmony's injected private fields (___name) belong to the patched type.
    # shortcut: the type is the last method target above it in the file; a patch class with
    # several target types is checked against one of them only.
    my @methods;
    while ($text =~ /\b(?:GameMember\.(?:Method|Delegate<[^(]*>)|AccessTools\.Method)\(\s*$TYPE|\[HarmonyPatch\(\s*$TYPE/g) {
        push @methods, [ $-[0], $1 // $2 ];
    }
    while ($text =~ /\b___(\w+)\b/g) {
        my $pos = $-[0];
        my @before = grep { $_->[0] < $pos } @methods;
        $add->('field', $before[-1][1], $1, undef, $pos) if @before;
    }
}

# --- 2. index a decompile folder: simple type name -> files declaring it ----------------------
# An unqualified name in the mod (InputAction) only matches a top-level game type, not a nested
# one (InputSpriteData.InputAction); Outer.Inner matches either.
sub type_files {
    my ($index, $type) = @_;
    if ($type eq '*') {    # any type
        my %all = map { %{ $index->{$_} } } grep { $_ ne '<base>' } keys %$index;
        my @all = sort keys %all;
        return @all;
    }
    my $files = $index->{ last_segment($type) } // {};
    my @files = sort grep { $type =~ /\./ || !$files->{$_} } keys %$files;
    return @files;
}

sub index_decomp {
    my $dir = shift;
    my (%types, %bases);
    for my $file (files_under($dir, qr/\.cs$/)) {
        my $text = slurp($file);
        while ($text =~ /^([ \t]*)(?:(?:public|private|protected|internal|static|sealed|abstract|partial|readonly|unsafe|ref|new)\s+)*(?:class|struct|interface|enum|record)\s+(\w+)(?:<[^>]*>)?(?:\s*:\s*([\w.]+))?/mg) {
            $types{$2}{$file} = length($1);
            $bases{$2} //= last_segment($3) if defined $3;
        }
    }
    $types{'<base>'} = \%bases;    # type -> its base class (or first interface), for inherited members
    return \%types;
}

my %cache;
sub lines_of { my $f = shift; $cache{$f} //= [ split /\n/, slurp($f) ]; return $cache{$f} }

# Declarations of `member` in the files that declare `type`: [file, line index, kind, text].
sub declarations {
    my ($index, $type, $member, $depth) = @_;
    my @found;
    for my $file (type_files($index, $type)) {
        my $lines = lines_of($file);
        for my $i (0 .. $#$lines) {
            my $l = $lines->[$i];
            next unless $l =~ /^\s*(?:public|private|protected|internal)\b[^=(]*?\b\Q$member\E\s*(\(|<|;|=|\{|$)/;
            my $kind = ($1 eq '(' || $1 eq '<') ? 'method' : 'field';
            push @found, [ $file, $i, $kind, $l ];
        }
    }
    my $base = $index->{'<base>'}{ last_segment($type) };
    return declarations($index, $base, $member, ($depth // 0) + 1) if !@found && $base && ($depth // 0) < 8;
    return @found;
}

# A method's declaration line plus its body (brace matched), or the line alone for fields.
sub body {
    my ($decl) = @_;
    my ($file, $i) = @$decl;
    my $lines = lines_of($file);
    my $out = $lines->[$i] . "\n";
    return $out if $lines->[$i] =~ /;\s*$/ || $i == $#$lines || $lines->[ $i + 1 ] !~ /^\s*\{/;
    my $depth = 0;
    for my $j ($i + 1 .. $#$lines) {
        $out .= $lines->[$j] . "\n";
        $depth += () = $lines->[$j] =~ /\{/g;
        $depth -= () = $lines->[$j] =~ /\}/g;
        last if $depth <= 0;
    }
    return $out;
}

sub param_types {
    my $line = shift;
    return undef unless $line =~ /\((.*)\)/;
    my $list = $1;
    my @p;
    my ($cur, $depth) = ('', 0);
    for my $c (split //, $list) {
        $depth++ if $c eq '<' || $c eq '[';
        $depth-- if $c eq '>' || $c eq ']';
        if ($c eq ',' && $depth == 0) { push @p, $cur; $cur = '' } else { $cur .= $c }
    }
    push @p, $cur if $cur =~ /\S/;
    return [ map {
        my $s = $_;
        $s =~ s/\[[^\]]*\]//g;    # attributes
        $s =~ s/=.*//;            # default values
        my $byref = $s =~ s/\b(?:ref|out|in)\s+//;
        $s =~ s/\b(?:params|this)\s+//;
        $s =~ s/^\s+|\s+$//g;
        $s =~ s/\s+\w+$//;        # parameter name
        last_segment($s) . ($byref ? '&' : '');
    } @p ];
}

my %alias = (Boolean => 'bool', Int32 => 'int', Single => 'float', String => 'string', Object => 'object');
sub same_params {
    my ($want, $have) = @_;
    return 0 unless $have && @$want == @$have;
    for my $k (0 .. $#$want) {
        my ($w, $h) = ($want->[$k], $have->[$k]);
        $w =~ s/(\w+)/$alias{$1} \/\/ $1/e;
        return 0 unless $w eq $h;
    }
    return 1;
}

# --- 3. check every target ------------------------------------------------------------------
my $newIndex = index_decomp($new);
my $oldIndex = $old ? index_decomp($old) : undef;
my ($bad, %seen) = (0);
my %counts;
for my $t (sort { $a->[1] cmp $b->[1] || ($a->[2] // '') cmp ($b->[2] // '') || $a->[4] cmp $b->[4] } @targets) {
    my ($kind, $type, $member, $params, $where) = @$t;
    my $label = ($type eq '*' ? 'RPC' : last_segment($type)) . (defined $member ? ".$member" : '') . ($params ? '(' . join(', ', @$params) . ')' : '');
    next if $seen{"$label|$where"}++;
    my $status;
    if (!type_files($newIndex, $type)) {
        # Without a baseline a type outside the decompile (Unity, Photon, SteamCommon) is external.
        $status = $oldIndex && type_files($oldIndex, $type) ? 'MISSING-TYPE' : 'external';
    } elsif (!defined $member) {
        $status = 'ok';
    } else {
        my @decl = declarations($newIndex, $type, $member);
        my @methods = grep { $_->[2] eq 'method' } @decl;
        if (!@decl) {
            # Not in the baseline either: a same-named type outside the game (e.g. InputAction).
            $status = $oldIndex && !declarations($oldIndex, $type, $member) ? 'external' : 'MISSING-MEMBER';
        } elsif ($params) {
            my @match = grep { same_params($params, param_types($_->[3])) } @methods;
            $status = @match ? 'ok' : 'PARAMS-MISMATCH';
            @decl = @match if @match;
        } elsif ($kind eq 'method' && @methods > 1) {
            # AccessTools.Method by name alone throws on overloads
            $status = 'AMBIGUOUS';
        } else {
            $status = 'ok';
        }
        if ($status eq 'ok' && $oldIndex) {
            my @was = declarations($oldIndex, $type, $member);
            @was = grep { same_params($params, param_types($_->[3])) } @was if $params;
            my $sig = sub { my $s = $_[0][3]; $s =~ s/^\s+|\s+$//g; $s };
            if (!@was) {
                $status = 'new';
            } elsif ($sig->($was[0]) ne $sig->($decl[0])) {
                $status = 'SIGNATURE-CHANGED';
            } elsif (body($was[0]) ne body($decl[0])) {
                $status = 'body-changed';
            }
        }
    }
    $counts{$status}++;
    $bad++ if $status =~ /^(MISSING|PARAMS|AMBIGUOUS)/;
    printf "%-17s %-70s %s\n", $status, $label, $where;
}
print join(', ', map { "$counts{$_} $_" } sort keys %counts), "\n";
exit($bad ? 1 : 0);
