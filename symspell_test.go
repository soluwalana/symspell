package main

import (
	"testing"
	"github.com/stretchr/testify/assert"
)

func TestSymSpell(t *testing.T) {
	assert := assert.New(t)
	ss, err := NewSymSpell()
	assert.Nil(err)
	assert.NotNil(ss)
	assert.Equal(LevDistance("hello", "elo"), 2, "elo == 2")
	assert.Equal(LevDistance("hello", "ello"), 1, "ello == 1")
	assert.Equal(LevDistance("hello", "hll"), 2, "hll == 2")
	assert.Equal(LevDistance("hello", "helo"), 1, "helo == 1")
	assert.Equal(LevDistance("hello", "heo"), 2, "heo == 2")
	assert.Equal(LevDistance("hello", "hel"), 2, "hel")
	assert.Equal(LevDistance("hello", "zello"), 1, "zello == 1")
	assert.Equal(LevDistance("hello", "ell"), 2, "ell == 2")
	assert.Equal(LevDistance("hello", "hllo"), 1, "hllo == 1")
	assert.Equal(LevDistance("hello", "hlo"), 2, "hlo == 2")
	assert.Equal(LevDistance("hello", "hell"), 1, "hell == 1")
	assert.Equal(LevDistance("hello", "hello"), 0, "hello == 0")
	assert.Equal(LevDistance("hello", "hello!"), 1, "hello! == 1")
	assert.Equal(LevDistance("hello", "hello WORLD!"), 7, "hello WORLD! == 1")

	ss.AddEntry("hello", []string{"a greeting",})
	ss.AddEntry("something", []string{"An item that exists",})
	
	assert.True(len(ss.Lookup("hllo")) > 0, "hllo must result in more than 0 entries returned in lookup")
	assert.Equal(ss.Lookup("hllo")[0].Term, "hello", "Should have responded with hello")
	assert.True(len(ss.Lookup("zello")) > 0, "zello must result in more than 0 entries")
	assert.Equal(ss.Lookup("zello")[0].Term, "hello", "should have responded with hello")
	assert.True(len(ss.Lookup("zllo")) > 0, "zllo must result in more than 0 entries returned")
	assert.Equal(ss.Lookup("zllo")[0].Term, "hello", "should have responded with hello")
}
